namespace MsftFoundryTeamsAudioSummarize.Web.Configuration;

/// <summary>
/// App registration #2: identity used to call Microsoft Foundry (Speech to Text + Agent Service).
/// Requires "Cognitive Services Speech User" and "Foundry User" roles on the Foundry resource.
/// </summary>
public sealed class FoundrySettings
{
    public const string SectionName = "Foundry";

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    /// <summary>e.g. https://&lt;foundry-resource&gt;.services.ai.azure.com/api/projects/&lt;foundry-project&gt;</summary>
    public required string ProjectEndpoint { get; init; }

    /// <summary>Name of the agent created in the Foundry portal.</summary>
    public required string AgentName { get; init; }

    /// <summary>e.g. https://<foundry-resource>.cognitiveservices.azure.com/</summary>
    public required string SpeechEndpoint { get; init; }

    /// <summary>Optional candidate locales (e.g. "pt-BR", "en-US"). Empty = automatic language detection.</summary>
    public IReadOnlyList<string> SpeechLocales { get; init; } = [];

    public static FoundrySettings Create(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new FoundrySettings
        {
            TenantId = ConfigurationKeys.Required(section, "TenantId", SectionName),
            ClientId = ConfigurationKeys.Required(section, "ClientId", SectionName),
            ClientSecret = ConfigurationKeys.Required(section, "ClientSecret", SectionName),
            ProjectEndpoint = ConfigurationKeys.Required(section, "ProjectEndpoint", SectionName),
            AgentName = ConfigurationKeys.Required(section, "AgentName", SectionName),
            SpeechEndpoint = ConfigurationKeys.Required(section, "SpeechEndpoint", SectionName),
            SpeechLocales = section.GetSection("SpeechLocales").Get<string[]>() ?? []
        };
    }
}
