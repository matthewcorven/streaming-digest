using Microsoft.Extensions.Configuration;

namespace StreamingDigest.Infrastructure.Extensions;

/// <summary>
/// Configuration helper extensions for streaming-digest database connection resolution.
/// Centralizes connection string lookup logic to reduce duplication across services.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Resolves the streaming-digest database connection string from configuration.
    /// Tries in order: streamingdigest → postgres → Default
    /// Returns empty string if no connection string is found.
    /// </summary>
    public static string GetStreamingDigestConnectionString(this IConfiguration configuration)
    {
        return configuration.GetConnectionString("streamingdigest")
            ?? configuration.GetConnectionString("postgres")
            ?? configuration.GetConnectionString("Default")
            ?? string.Empty;
    }
}
