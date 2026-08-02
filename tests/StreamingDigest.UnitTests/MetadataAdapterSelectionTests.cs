using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.UnitTests;

public sealed class MetadataAdapterSelectionTests
{
    private readonly YtDlpMetadataAdapter _ytDlpAdapter = new();
    private readonly NullLogger<MetadataAdapterSelector> _logger = NullLogger<MetadataAdapterSelector>.Instance;

    // ── Adapter Selection: YouTube API Preferred + Available ────────────────────────

    [Fact]
    public void ActiveMetadataSource_returns_youtube_api_when_preferred_and_api_key_available()
    {
        var httpClient = new HttpClient();
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(httpClient, "test-api-key");
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "youtube_api",
                YouTubeApiKey = "test-api-key"
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("youtube_api", selector.ActiveMetadataSource);
        Assert.NotNull(selector.YouTubeApiAdapter);
        Assert.True(selector.YouTubeApiAdapter.IsConfigured);
    }

    [Fact]
    public void ActiveMetadataSource_returns_ytdlp_when_youtube_api_preferred_but_no_key()
    {
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "youtube_api",
                YouTubeApiKey = null
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("ytdlp", selector.ActiveMetadataSource);
        Assert.NotNull(selector.YouTubeApiAdapter);
        Assert.False(selector.YouTubeApiAdapter.IsConfigured);
    }

    [Fact]
    public void ActiveMetadataSource_returns_ytdlp_when_preferred()
    {
        var httpClient = new HttpClient();
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(httpClient, "test-api-key");
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "ytdlp",
                YouTubeApiKey = "test-api-key"
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("ytdlp", selector.ActiveMetadataSource);
    }

    [Fact]
    public void ActiveMetadataSource_case_insensitive_prefers_youtube_api()
    {
        var httpClient = new HttpClient();
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(httpClient, "test-api-key");
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "YOUTUBE_API",
                YouTubeApiKey = "test-api-key"
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("youtube_api", selector.ActiveMetadataSource);
    }

    [Fact]
    public void ActiveMetadataSource_case_insensitive_prefers_ytdlp()
    {
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "YTDLP",
                YouTubeApiKey = null
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("ytdlp", selector.ActiveMetadataSource);
    }

    [Fact]
    public void ActiveMetadataSource_falls_back_to_ytdlp_for_unknown_preference()
    {
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);
        var config = new ApplicationConfiguration
        {
            Ingestion = new IngestionSettings
            {
                PreferredMetadataSource = "unknown_source",
                YouTubeApiKey = null
            }
        };

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.Equal("ytdlp", selector.ActiveMetadataSource);
    }

    // ── Adapter Access ──────────────────────────────────────────────────────────────

    [Fact]
    public void YtDlpAdapter_is_always_accessible()
    {
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);
        var config = new ApplicationConfiguration();

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.NotNull(selector.YtDlpAdapter);
        Assert.Same(_ytDlpAdapter, selector.YtDlpAdapter);
    }

    [Fact]
    public void YouTubeApiAdapter_is_accessible_when_provided()
    {
        var httpClient = new HttpClient();
        var youtubeApiAdapter = new YouTubeApiMetadataAdapter(httpClient, "test-api-key");
        var config = new ApplicationConfiguration();

        var selector = new MetadataAdapterSelector(_ytDlpAdapter, youtubeApiAdapter, config, _logger);

        Assert.NotNull(selector.YouTubeApiAdapter);
        Assert.Same(youtubeApiAdapter, selector.YouTubeApiAdapter);
    }

    // ── Configuration Defaults ──────────────────────────────────────────────────────

    [Fact]
    public void Default_preferred_source_is_youtube_api()
    {
        var config = new ApplicationConfiguration();
        Assert.Equal("youtube_api", config.Ingestion.PreferredMetadataSource);
    }

    [Fact]
    public void Default_min_duration_seconds_is_61()
    {
        var config = new ApplicationConfiguration();
        Assert.Equal(61, config.Ingestion.MinDurationSeconds);
    }

    [Fact]
    public void Default_max_age_days_is_30()
    {
        var config = new ApplicationConfiguration();
        Assert.Equal(30, config.Ingestion.DefaultMaxAgeDays);
    }
}
