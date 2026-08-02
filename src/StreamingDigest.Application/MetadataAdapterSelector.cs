using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.Application;

/// <summary>
/// Selects the appropriate metadata adapter (YtDlp or YouTube API) based on configuration
/// with graceful fallback when API keys are unavailable.
/// 
/// Selection strategy:
/// 1. If preferredMetadataSource is "youtube_api" and API key is configured, use YouTube API
/// 2. If YouTube API is unavailable or preferred source is "ytdlp", use YtDlp
/// 3. Always attempt to provide a working adapter; never return null
/// </summary>
public interface IMetadataAdapterSelector
{
    /// <summary>
    /// Gets the active metadata source being used.
    /// </summary>
    string ActiveMetadataSource { get; }

    /// <summary>
    /// Gets the YouTube API metadata adapter if available and configured.
    /// </summary>
    YouTubeApiMetadataAdapter? YouTubeApiAdapter { get; }

    /// <summary>
    /// Gets the YtDlp metadata adapter (always available as fallback).
    /// </summary>
    YtDlpMetadataAdapter YtDlpAdapter { get; }
}

public sealed class MetadataAdapterSelector : IMetadataAdapterSelector
{
    private readonly ILogger<MetadataAdapterSelector> _logger;
    private readonly YouTubeApiMetadataAdapter? _youtubeApiAdapter;
    private readonly YtDlpMetadataAdapter _ytDlpAdapter;
    private readonly string _activeSource;

    public string ActiveMetadataSource => _activeSource;

    public YouTubeApiMetadataAdapter? YouTubeApiAdapter => _youtubeApiAdapter;

    public YtDlpMetadataAdapter YtDlpAdapter => _ytDlpAdapter;

    public MetadataAdapterSelector(
        YtDlpMetadataAdapter ytDlpAdapter,
        YouTubeApiMetadataAdapter? youtubeApiAdapter,
        ApplicationConfiguration config,
        ILogger<MetadataAdapterSelector> logger)
    {
        ArgumentNullException.ThrowIfNull(ytDlpAdapter);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _ytDlpAdapter = ytDlpAdapter;
        _youtubeApiAdapter = youtubeApiAdapter;
        _logger = logger;

        _activeSource = ResolveActiveSource(config, youtubeApiAdapter);
        
        _logger.LogInformation(
            "Metadata adapter selector initialized: active={ActiveSource}, youtubeApiAvailable={YoutubeApiAvailable}",
            _activeSource,
            youtubeApiAdapter?.IsConfigured ?? false);
    }

    private string ResolveActiveSource(ApplicationConfiguration config, YouTubeApiMetadataAdapter? youtubeApiAdapter)
    {
        var preferred = config.Ingestion.PreferredMetadataSource ?? "youtube_api";

        if (string.Equals(preferred, "youtube_api", StringComparison.OrdinalIgnoreCase))
        {
            if (youtubeApiAdapter?.IsConfigured ?? false)
            {
                _logger.LogDebug("Using YouTube API adapter (preferred source available with API key)");
                return "youtube_api";
            }

            _logger.LogWarning(
                "Preferred metadata source is YouTube API but API key is not configured; falling back to YtDlp");
            return "ytdlp";
        }

        if (string.Equals(preferred, "ytdlp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Using YtDlp adapter (explicitly preferred)");
            return "ytdlp";
        }

        _logger.LogWarning(
            "Unknown preferred metadata source '{PreferredSource}'; using YtDlp as fallback",
            preferred);
        return "ytdlp";
    }
}
