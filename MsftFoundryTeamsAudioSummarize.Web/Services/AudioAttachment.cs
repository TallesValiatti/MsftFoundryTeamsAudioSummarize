using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>
/// An audio file sent by the user, resolved from a Teams/Bot Framework attachment.
/// </summary>
/// <param name="FileName">Original file name.</param>
/// <param name="DownloadUrl">URL used to download the file.</param>
/// <param name="RequiresBotToken">
/// False for Teams file uploads (pre-authenticated downloadUrl).
/// True for inline attachments hosted by the Bot Connector (contentUrl).
/// </param>
public sealed record AudioAttachment(string FileName, Uri DownloadUrl, bool RequiresBotToken)
{
    private const string TeamsFileDownloadInfo = "application/vnd.microsoft.teams.file.download.info";

    // Formats accepted by the Speech "Transcribe" API (fast transcription / LLM Speech).
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "wav", "mp3", "ogg", "opus", "flac", "wma", "aac", "amr", "webm", "spx"
    };

    public static AudioAttachment? FindFirst(IEnumerable<Attachment>? attachments)
    {
        if (attachments is null)
            return null;

        foreach (var attachment in attachments)
        {
            var audio = TryCreate(attachment);

            if (audio is not null)
                return audio;
        }

        return null;
    }

    private static AudioAttachment? TryCreate(Attachment attachment)
    {
        // Files uploaded in Teams: { contentType: file.download.info, name, content: { downloadUrl, fileType } }
        if (string.Equals(attachment.ContentType, TeamsFileDownloadInfo, StringComparison.OrdinalIgnoreCase))
        {
            var content = attachment.Content is null ? null : JObject.FromObject(attachment.Content);
            var downloadUrl = content?.Value<string>("downloadUrl");
            var fileType = content?.Value<string>("fileType") ?? Path.GetExtension(attachment.Name)?.TrimStart('.');

            return IsSupported(fileType) && Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                ? new AudioAttachment(attachment.Name ?? $"audio.{fileType}", uri, RequiresBotToken: false)
                : null;
        }

        // Inline audio (audio/* content type) hosted by the Bot Connector.
        if (attachment.ContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true
            && Uri.TryCreate(attachment.ContentUrl, UriKind.Absolute, out var contentUri))
        {
            var extension = Path.GetExtension(attachment.Name)?.TrimStart('.');

            if (!string.IsNullOrEmpty(extension) && !IsSupported(extension))
                return null;

            return new AudioAttachment(attachment.Name ?? "audio", contentUri, RequiresBotToken: true);
        }

        return null;
    }

    /// <summary>
    /// In channels and group chats Teams shares files as "reference" attachments (a SharePoint/OneDrive link)
    /// instead of a downloadable file. Reading them requires Microsoft Graph, so they are detected only to
    /// reply with a helpful message.
    /// </summary>
    public static bool HasAudioReference(IEnumerable<Attachment>? attachments) =>
        attachments?.Any(attachment =>
            string.Equals(attachment.ContentType, "reference", StringComparison.OrdinalIgnoreCase)
            && IsSupported(Path.GetExtension(attachment.Name)?.TrimStart('.'))) == true;

    private static bool IsSupported(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
}
