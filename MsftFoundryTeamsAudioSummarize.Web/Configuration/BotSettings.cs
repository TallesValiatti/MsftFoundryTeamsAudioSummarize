namespace MsftFoundryTeamsAudioSummarize.Web.Configuration;

/// <summary>
/// App registration #1: Azure Bot Service identity (Teams manifest, Bot Service channel and /api/messages JWT validation).
/// Read from the flat keys the Bot Framework SDK expects (MicrosoftAppType, MicrosoftAppId, MicrosoftAppPassword, MicrosoftAppTenantId).
/// </summary>
public sealed class BotSettings
{
    public required string AppType { get; init; }

    public required string AppId { get; init; }

    public required string AppPassword { get; init; }

    public required string TenantId { get; init; }

    public static BotSettings Create(IConfiguration configuration) => new()
    {
        AppType = configuration["MicrosoftAppType"] ?? "SingleTenant",
        AppId = ConfigurationKeys.Required(configuration, "MicrosoftAppId"),
        AppPassword = ConfigurationKeys.Required(configuration, "MicrosoftAppPassword"),
        TenantId = ConfigurationKeys.Required(configuration, "MicrosoftAppTenantId")
    };
}
