using Azure.AI.Speech.Transcription;
using Azure.Identity;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>
/// Converts audio to text with Microsoft Foundry Speech to Text (Azure.AI.Speech.Transcription),
/// authenticated with the Foundry app registration (Cognitive Services Speech User role).
/// </summary>
public sealed class SpeechTranscriptionService
{
    private readonly TranscriptionClient _client;
    private readonly IReadOnlyList<string> _locales;

    public SpeechTranscriptionService(FoundrySettings settings, ClientSecretCredential credential)
    {
        _client = new TranscriptionClient(new Uri(settings.SpeechEndpoint), credential);
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
}
