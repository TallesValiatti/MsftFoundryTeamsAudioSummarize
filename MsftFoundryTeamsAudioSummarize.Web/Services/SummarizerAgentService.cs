using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;
using OpenAI.Responses;

namespace MsftFoundryTeamsAudioSummarize.Web.Services;

/// <summary>
/// Sends the transcript to the Foundry agent (Responses API) and returns the summary.
/// Single-turn: no previousResponseId, every audio is summarized independently.
/// </summary>
public sealed class SummarizerAgentService
{
    private readonly ProjectResponsesClient _responsesClient;

    public SummarizerAgentService(FoundrySettings settings, ClientSecretCredential credential)
    {
        var projectClient = new AIProjectClient(new Uri(settings.ProjectEndpoint), credential);

        _responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForAgent(settings.AgentName);
    }

    public async Task<string> SummarizeAsync(string transcript, CancellationToken cancellationToken)
    {
        var result = await _responsesClient.CreateResponseAsync(
            userInputText: transcript,
            cancellationToken: cancellationToken);

        return result.Value.GetOutputText()?.Trim() ?? string.Empty;
    }
}
