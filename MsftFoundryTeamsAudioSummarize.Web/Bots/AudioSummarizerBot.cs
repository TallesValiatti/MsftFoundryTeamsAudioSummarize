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
    SpeechTranscriptionService transcriptionService,
    SummarizerAgentService summarizerAgent,
    CloudAdapter adapter,
    BotSettings botSettings,
    ILogger<AudioSummarizerBot> logger) : ActivityHandler
{
    internal const string NotAudioMessage =
        "I only summarize audio. Please send an audio file (wav, mp3, ogg, opus, flac, wma, aac, amr, webm).";

    internal const string SharedFileMessage =
        "Teams doesn't share files from channels or group chats with bots. Please send the audio to me in a personal chat.";

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

        if (audio is null)
        {
            var isSharedFile = AudioAttachment.HasAudioReference(activity.Attachments);

            logger.LogInformation(
                "No downloadable audio in conversation {ConversationId} ({ConversationType}), SharedFileReference={IsSharedFile}.",
                activity.Conversation.Id,
                activity.Conversation.ConversationType,
                isSharedFile);

            await turnContext.SendActivityAsync(
                MessageFactory.Text(isSharedFile ? SharedFileMessage : NotAudioMessage),
                cancellationToken);
            return;
        }

        logger.LogInformation(
            "Audio '{FileName}' received in conversation {ConversationId} ({ConversationType}).",
            audio.FileName,
            activity.Conversation.Id,
            activity.Conversation.ConversationType);

        // Captured before the request completes: the TurnContext is disposed once ProcessAsync returns.
        var conversationReference = activity.GetConversationReference();

        await turnContext.SendActivityAsync(MessageFactory.Text(ProcessingMessage), cancellationToken);
        await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken);

        // Azure Bot Service times out after ~15 s, so the long-running work runs in the background
        // (detached from the request token) and the summary is delivered proactively.
        _ = Task.Run(
            () => ProcessAudioAsync(audio, conversationReference),
            CancellationToken.None);
    }

    private async Task ProcessAudioAsync(AudioAttachment audio, ConversationReference conversationReference)
    {
        var conversationId = conversationReference.Conversation.Id;
        string reply;

        try
        {
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
                audio.FileName,
                conversationId,
                ex.Status,
                ex.GetRawResponse()?.Content?.ToString());
            reply = FailureMessage;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to summarize '{FileName}' for conversation {ConversationId}.", audio.FileName, conversationId);
            reply = FailureMessage;
        }

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
            logger.LogError(ex, "Failed to deliver the reply to conversation {ConversationId}.", conversationId);
        }
    }
}
