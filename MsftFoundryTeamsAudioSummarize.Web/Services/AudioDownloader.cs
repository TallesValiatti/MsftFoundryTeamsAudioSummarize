using System.Net.Http.Headers;
using Microsoft.Bot.Connector.Authentication;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>
/// Downloads the audio attachment into memory.
/// </summary>
public sealed class AudioDownloader(
    IHttpClientFactory httpClientFactory,
    BotSettings botSettings)
{
    public const long MaxAudioBytes = 200L * 1024 * 1024;

    // The bot token is only sent to Bot Connector hosts, never to arbitrary URLs.
    private static readonly string[] BotConnectorHosts = ["botframework.com", "trafficmanager.net"];

    // MicrosoftAppCredentials caches and refreshes the Bot Connector token internally.
    private readonly MicrosoftAppCredentials _botCredentials =
        new(botSettings.AppId, botSettings.AppPassword, botSettings.TenantId);

    public async Task<MemoryStream> DownloadAsync(AudioAttachment audio, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, audio.DownloadUrl);

        if (audio.RequiresBotToken && IsBotConnectorHost(audio.DownloadUrl))
        {
            var token = await _botCredentials.GetTokenAsync();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var httpClient = httpClientFactory.CreateClient(nameof(AudioDownloader));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxAudioBytes)
            throw new AudioTooLargeException();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);

        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxAudioBytes)
                throw new AudioTooLargeException();

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static bool IsBotConnectorHost(Uri uri) =>
        BotConnectorHosts.Any(host => uri.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase));
}

public sealed class AudioTooLargeException() : Exception("The audio file exceeds the maximum supported size.");
