using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;
using MsftFoundryTeamsAudioSummarize.Web.Services;

namespace MsftFoundryTeamsAudioSummarize.Web.Bots;

/// <summary>
/// Teams -> Azure Bot Service -> this bot.
/// Accepts only audio attachments: audio -> Speech to Text -> Foundry agent -> summary back to Teams.
/// </summary>
public sealed class AudioSummarizerBot(
    AudioDownloader audioDownloader,
    GraphAudioService graphAudio,
    SpeechTranscriptionService transcriptionService,
    SummarizerAgentService summarizerAgent,
    CloudAdapter adapter,
    BotSettings botSettings,
    ILogger<AudioSummarizerBot> logger) : ActivityHandler
{
    internal const string NotAudioMessage =
        "I only summarize audio. Please send an audio file (wav, mp3, ogg, opus, flac, wma, aac, amr, webm).";

    internal const string SharedFileMessage =
        "File access in this chat needs Microsoft Graph setup. Ask your administrator to enable it, or send the audio directly to me in a personal chat.";

    internal const string ProcessingMessage = "Got it! Transcribing and summarizing your audio...";

    internal const string NoSpeechMessage = "I couldn't detect any speech in this audio.";

    internal const string TooLargeMessage = "This audio file is too large. The maximum size is 200 MB.";

    internal const string FailureMessage = "Sorry, I couldn't summarize this audio. Please try again later.";

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;
        var audio = AudioAttachment.FindFirst(activity.Attachments);

        var sharedConversation = GraphAudioService.IsSharedConversation(activity);
        if (audio is null && (!sharedConversation || !graphAudio.Enabled))
        {
            await turnContext.SendActivityAsync(
                MessageFactory.Text(sharedConversation || AudioAttachment.HasAudioReference(activity.Attachments)
                    ? SharedFileMessage : NotAudioMessage), cancellationToken);
            return;
        }

        // Copy routing data before the TurnContext is disposed; Graph calls happen in the background.
        string? messagePath = null;
        if (audio is null)
        {
            try { messagePath = GraphAudioService.GetMessagePath(activity); }
            catch (GraphAudioException ex)
            {
                logger.LogWarning(ex, "Cannot resolve Teams message routing.");
                await turnContext.SendActivityAsync(MessageFactory.Text(FailureMessage), cancellationToken);
                return;
            }
        }

        // Captured before the request completes: the TurnContext is disposed once ProcessAsync returns.
        var conversationReference = activity.GetConversationReference();

        await turnContext.SendActivityAsync(MessageFactory.Text(audio is null ? "Checking this message for an audio file..." : ProcessingMessage), cancellationToken);
        await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken);

        // Azure Bot Service times out after ~15 s, so the long-running work runs in the background
        // (detached from the request token) and the summary is delivered proactively.
        _ = Task.Run(
            () => ProcessAudioAsync(audio, messagePath, conversationReference),
            CancellationToken.None);
    }

    private async Task ProcessAudioAsync(AudioAttachment? audio, string? messagePath, ConversationReference conversationReference)
    {
        var conversationId = conversationReference.Conversation.Id;
        string reply;

        try
        {
            if (audio is null)
                audio = await graphAudio.ResolveAsync(messagePath!, CancellationToken.None);

            if (audio is null)
            {
                await SendReplyAsync("No audio file was attached to this message. Attach the audio and @mention me in the same message.", conversationReference);
                return;
            }

            await using var audioStream = await audioDownloader.DownloadAsync(audio, CancellationToken.None);
            logger.LogInformation("Downloaded '{FileName}' ({Bytes} bytes).", audio.FileName, audioStream.Length);

            var transcript = await transcriptionService.TranscribeAsync(audioStream, CancellationToken.None);
            logger.LogInformation("Transcribed '{FileName}' ({Characters} characters).", audio.FileName, transcript.Length);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                reply = NoSpeechMessage;
            }
            else
            {
                var summary = await summarizerAgent.SummarizeAsync(transcript, CancellationToken.None);
                reply = string.IsNullOrWhiteSpace(summary) ? FailureMessage : summary;
            }
        }
        catch (GraphAudioException ex)
        {
            logger.LogWarning("Graph audio lookup failed in {ConversationId}: {Reason}", conversationId, ex.Message);
            reply = ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized
                ? "I couldn't access this chat or file. Ask your administrator to check the bot's Microsoft Graph permissions and admin consent. You can also send the audio directly to me."
                : "I couldn't retrieve the shared file. Please resend the audio with an @mention, or send it directly to me.";
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            logger.LogError(ex, "Authentication failed while processing audio in {ConversationId}.", conversationId);
            reply = FailureMessage;
        }
        catch (AudioTooLargeException)
        {
            reply = TooLargeMessage;
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            // The exception message omits the service error body (e.g. the reason for a 401/403).
            logger.LogError(
                ex,
                "Foundry call failed for '{FileName}' in conversation {ConversationId}. Status={Status}, Body={Body}",
                audio?.FileName,
                conversationId,
                ex.Status,
                ex.GetRawResponse()?.Content?.ToString());
            reply = FailureMessage;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to summarize '{FileName}' for conversation {ConversationId}.", audio?.FileName, conversationId);
            reply = FailureMessage;
        }

        await SendReplyAsync(reply, conversationReference);
    }

    private async Task SendReplyAsync(string reply, ConversationReference conversationReference)
    {
        try
        {
            // The string botAppId overload is required for proactive replies outside the original turn.
            await adapter.ContinueConversationAsync(
                botSettings.AppId,
                conversationReference,
                (proactiveContext, ct) => proactiveContext.SendActivityAsync(MessageFactory.Text(reply), ct),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver the reply to conversation {ConversationId}.", conversationReference.Conversation.Id);
        }
    }
}
