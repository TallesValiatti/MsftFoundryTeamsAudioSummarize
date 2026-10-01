using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using Azure.AI.Speech.Transcription;
using Azure.Identity;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>
/// Converts audio to text with Microsoft Foundry Speech to Text (Azure.AI.Speech.Transcription),
/// authenticated with the Foundry app registration ("Foundry User" role on the Foundry resource).
/// </summary>
public sealed class SpeechTranscriptionService
{
    private readonly TranscriptionClient _client;
    private readonly IReadOnlyList<string> _locales;

    public SpeechTranscriptionService(
        FoundrySettings settings,
        ClientSecretCredential credential,
        ILogger<SpeechTranscriptionService> logger)
    {
        var options = new TranscriptionClientOptions();
        options.AddPolicy(new AccessDeniedLoggingPolicy(logger), PipelinePosition.BeforeTransport);

        _client = new TranscriptionClient(new Uri(settings.SpeechEndpoint), credential, options);
        _locales = settings.SpeechLocales;
    }

    public async Task<string> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        var options = new TranscriptionOptions(audio);

        foreach (var locale in _locales)
            options.Locales.Add(locale);

        var response = await _client.TranscribeAsync(options, cancellationToken);

        return string.Join(
            Environment.NewLine,
            response.Value.CombinedPhrases.Select(phrase => phrase.Text)).Trim();
    }

    /// <summary>
    /// On 401/403, logs the request actually sent to Speech (URL, content type and token claims, no secrets).
    /// </summary>
    private sealed class AccessDeniedLoggingPolicy(ILogger logger) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            ProcessNext(message, pipeline, currentIndex);
            LogIfDenied(message);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            await ProcessNextAsync(message, pipeline, currentIndex);
            LogIfDenied(message);
        }

        private void LogIfDenied(PipelineMessage message)
        {
            if (message.Response?.Status is not (401 or 403))
                return;

            message.Request.Headers.TryGetValue("Authorization", out var authorization);
            message.Request.Headers.TryGetValue("Content-Type", out var contentType);

            logger.LogWarning(
                "Speech denied {Method} {Uri} (Content-Type={ContentType}). Token: {Claims}",
                message.Request.Method,
                message.Request.Uri,
                contentType,
                DescribeToken(authorization));
        }

        private static string DescribeToken(string? authorization)
        {
            if (string.IsNullOrWhiteSpace(authorization))
                return "<no Authorization header>";

            try
            {
                var parts = authorization.Split(' ', 2);
                var payload = parts[^1].Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload += new string('=', (4 - payload.Length % 4) % 4);

                using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                string? Claim(string name) => claims.RootElement.TryGetProperty(name, out var value) ? value.ToString() : null;

                return $"scheme={parts[0]}, aud={Claim("aud")}, tid={Claim("tid")}, appid={Claim("appid")}, oid={Claim("oid")}, idtyp={Claim("idtyp")}";
            }
            catch (Exception ex)
            {
                return $"<unreadable: {ex.GetType().Name}>";
            }
        }
    }
}
