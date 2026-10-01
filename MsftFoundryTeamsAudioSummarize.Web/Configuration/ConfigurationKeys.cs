namespace MsftFoundryTeamsAudioSummarize.Web.Configuration;

internal static class ConfigurationKeys
{
    public static string Required(IConfiguration configuration, string key, string? sectionName = null)
    {
        var value = configuration[key];
        var fullKey = sectionName is null ? key : $"{sectionName}:{key}";

        return string.IsNullOrWhiteSpace(value) || value.StartsWith('<')
            ? throw new InvalidOperationException($"Missing required configuration key '{fullKey}'.")
            : value;
    }
}
