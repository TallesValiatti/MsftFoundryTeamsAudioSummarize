using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>Resolves files on the triggering Teams message, including attachments omitted by the bot event.</summary>
public sealed class GraphAudioService(HttpClient client, TokenCredential credential, bool enabled)
{
    public bool Enabled => enabled;

    public static bool IsSharedConversation(IMessageActivity activity) =>
        string.Equals(activity.ChannelId, "msteams", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(activity.Conversation?.ConversationType, "groupChat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(activity.Conversation?.ConversationType, "channel", StringComparison.OrdinalIgnoreCase));

    public static string GetMessagePath(IMessageActivity activity)
    {
        static string Encode(string? value) => !string.IsNullOrWhiteSpace(value)
            ? Uri.EscapeDataString(value)
            : throw new GraphAudioException("The Teams message is missing routing information.");

        JObject data = activity.ChannelData is null ? new JObject() : JObject.FromObject((object)activity.ChannelData);
        if (string.Equals(activity.Conversation?.ConversationType, "channel", StringComparison.OrdinalIgnoreCase))
        {
            var path = $"teams/{Encode(data["team"]?["aadGroupId"]?.Value<string>())}/channels/{Encode(data["channel"]?["id"]?.Value<string>())}/messages/";
            return !string.IsNullOrEmpty(activity.ReplyToId) && activity.ReplyToId != activity.Id
                ? $"{path}{Encode(activity.ReplyToId)}/replies/{Encode(activity.Id)}"
                : path + Encode(activity.Id);
        }

        return $"chats/{Encode(activity.Conversation?.Id)}/messages/{Encode(activity.Id)}";
    }

    public async Task<AudioAttachment?> ResolveAsync(string messagePath, CancellationToken cancellationToken)
    {
        if (!Enabled)
            throw new GraphAudioException("Microsoft Graph file access is disabled.");

        var message = await GetAsync(messagePath, cancellationToken);
        foreach (var attachment in message["attachments"] as JArray ?? new JArray())
        {
            var name = attachment.Value<string>("name");
            if (!string.Equals(attachment.Value<string>("contentType"), "reference", StringComparison.OrdinalIgnoreCase)
                || !AudioAttachment.IsSupportedFileName(name))
                continue;

            var url = attachment.Value<string>("contentUrl");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var fileUri) || fileUri.Scheme != Uri.UriSchemeHttps)
                continue;

            var shareId = "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url!))
                .TrimEnd('=').Replace('/', '_').Replace('+', '-');
            var item = await GetAsync($"shares/{shareId}/driveItem", cancellationToken);
            if (item.Value<long?>("size") > AudioDownloader.MaxAudioBytes)
                throw new AudioTooLargeException();

            // Recheck the actual file metadata rather than trusting the displayed attachment name.
            var actualName = item.Value<string>("name");
            if (item["file"] is null || !AudioAttachment.IsSupportedFileName(actualName))
                continue;

            if (!Uri.TryCreate(item.Value<string>("@microsoft.graph.downloadUrl"), UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps)
                throw new GraphAudioException("Microsoft Graph did not return a file download URL.");

            // The short-lived URL is preauthenticated; never forward the Graph token to it.
            return new AudioAttachment(actualName!, downloadUri, RequiresBotToken: false);
        }

        return null;
    }

    private async Task<JObject> GetAsync(string path, CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), cancellationToken);
        // Always build the request under the fixed Graph origin. No tokens go to attachment URLs.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new GraphAudioException($"Microsoft Graph returned HTTP {(int)response.StatusCode}.", response.StatusCode);

        return JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

public sealed class GraphAudioException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
