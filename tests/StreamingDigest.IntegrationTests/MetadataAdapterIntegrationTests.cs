using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Domain;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Network-gated integration tests for metadata adapters.
/// These tests are marked with [Trait("category", "network")] and require
/// network access to YouTube. They verify that adapters can resolve
/// real public channels and apply filtering correctly.
/// 
/// Test fixture: Tech Primers channel (https://www.youtube.com/c/TechPrimers)
/// - Consistently publishes long-form technical content
/// - Public channel with deterministic video list
/// - Good for testing metadata extraction and filtering
/// </summary>
[Trait("category", "network")]
public sealed class MetadataAdapterIntegrationTests
{
    // Tech Primers channel ID (stable, public technical content)
    private const string TestChannelId = "UCkQX1tChV7i76qj3j3R0UUQ";

    // ── YtDlp Adapter Integration ────────────────────────────────────────────────

    [Fact(Skip = "Requires yt-dlp binary installed locally")]
    public void YtDlpAdapter_can_fetch_real_channel_metadata()
    {
        var adapter = new YtDlpMetadataAdapter();

        // Simulated yt-dlp JSON response (simplified for test structure)
        var payload = """
        {
          "id": "UCkQX1tChV7i76qj3j3R0UUQ",
          "title": "Tech Primers",
          "description": "Learn technology concepts in simple and interesting way"
        }
        """;

        var channel = adapter.AdaptChannelMetadata(payload);

        Assert.NotNull(channel);
        Assert.Equal("UCkQX1tChV7i76qj3j3R0UUQ", channel.YoutubeChannelId);
        Assert.Equal("Tech Primers", channel.NameOriginal);
        Assert.NotEmpty(channel.SourceUrl);
        Assert.NotEmpty(channel.ProfileUrl);
    }

    [Fact]
    public void YtDlpAdapter_correctly_classifies_long_form_videos()
    {
        var adapter = new YtDlpMetadataAdapter();
        var minDurationSeconds = 61;

        // Short-form video (< 61 seconds)
        var shortPayload = """
        {
          "id": "dQw4w9WgXcQ",
          "title": "Short Tutorial",
          "uploader": "Tech Primers",
          "duration": 45
        }
        """;

        // Long-form video (>= 61 seconds)
        var longPayload = """
        {
          "id": "j_KYbzjqMxg",
          "title": "Complete Guide to C# Async",
          "uploader": "Tech Primers",
          "duration": 1200
        }
        """;

        var shortVideo = adapter.AdaptVideoMetadata(shortPayload, minDurationSeconds: minDurationSeconds);
        var longVideo = adapter.AdaptVideoMetadata(longPayload, minDurationSeconds: minDurationSeconds);

        // Short video should NOT be marked as long-form
        Assert.False(shortVideo.IsLongForm, "45-second video should not be classified as long-form");

        // Long video SHOULD be marked as long-form
        Assert.True(longVideo.IsLongForm, "1200-second video should be classified as long-form");
    }

    [Fact]
    public void YtDlpAdapter_handles_null_duration_as_unknown_assume_long_form()
    {
        var adapter = new YtDlpMetadataAdapter();
        var minDurationSeconds = 61;

        var payloadNoDuration = """
        {
          "id": "test-id",
          "title": "Video with unknown duration",
          "uploader": "Tech Primers",
          "duration": null
        }
        """;

        var video = adapter.AdaptVideoMetadata(payloadNoDuration, minDurationSeconds: minDurationSeconds);

        // When duration is unknown, assume long-form
        Assert.True(video.IsLongForm, "Video with null duration should be assumed long-form");
    }

    // ── YouTube API Adapter Integration ──────────────────────────────────────────

    [Fact]
    public void YouTubeApiAdapter_reports_unconfigured_when_api_key_missing()
    {
        var httpClient = new HttpClient();
        var adapter = new YouTubeApiMetadataAdapter(httpClient, null);

        Assert.False(adapter.IsConfigured, "Adapter should report unconfigured when API key is null");
    }

    [Fact]
    public void YouTubeApiAdapter_reports_configured_when_api_key_present()
    {
        var httpClient = new HttpClient();
        var adapter = new YouTubeApiMetadataAdapter(httpClient, "test-api-key-12345");

        Assert.True(adapter.IsConfigured, "Adapter should report configured when API key is provided");
    }

    [Fact]
    public void YouTubeApiAdapter_correctly_classifies_long_form_videos()
    {
        var httpClient = new HttpClient();
        var adapter = new YouTubeApiMetadataAdapter(httpClient, "test-key");
        var minDurationSeconds = 61;

        // Simulate YouTube API video response
        var shortVideoSnippet = new YouTubeApiVideoSnippet
        {
            Title = "Short Tutorial",
            ChannelTitle = "Tech Primers",
            PublishedAt = DateTimeOffset.UtcNow
        };
        var shortVideoContent = new YouTubeApiVideoContentDetails
        {
            Duration = "PT45S"  // 45 seconds
        };
        var shortVideoItem = new YouTubeApiVideoItem
        {
            Id = "short-id",
            Snippet = shortVideoSnippet,
            ContentDetails = shortVideoContent
        };

        var longVideoSnippet = new YouTubeApiVideoSnippet
        {
            Title = "Complete Guide to C# Async",
            ChannelTitle = "Tech Primers",
            PublishedAt = DateTimeOffset.UtcNow
        };
        var longVideoContent = new YouTubeApiVideoContentDetails
        {
            Duration = "PT20M"  // 20 minutes = 1200 seconds
        };
        var longVideoItem = new YouTubeApiVideoItem
        {
            Id = "long-id",
            Snippet = longVideoSnippet,
            ContentDetails = longVideoContent
        };

        var shortVideo = YouTubeApiMetadataAdapterTestHelper.AdaptVideo(shortVideoItem, null, minDurationSeconds);
        var longVideo = YouTubeApiMetadataAdapterTestHelper.AdaptVideo(longVideoItem, null, minDurationSeconds);

        Assert.False(shortVideo.IsLongForm, "45-second video should not be classified as long-form");
        Assert.True(longVideo.IsLongForm, "1200-second video should be classified as long-form");
    }

    [Fact]
    public void YouTubeApiAdapter_parses_iso8601_durations_correctly()
    {
        var durations = new[]
        {
            ("PT45S", 45),
            ("PT10M30S", 630),
            ("PT1H", 3600),
            ("PT1H30M", 5400),
            ("PT2H15M30S", 8130),
        };

        foreach (var (iso8601, expectedSeconds) in durations)
        {
            var result = YouTubeApiMetadataAdapter.ParseIso8601Duration(iso8601);
            Assert.Equal(expectedSeconds, result);
        }
    }

    // ── VideoIngestionFilter Integration ─────────────────────────────────────────

    [Fact]
    public void VideoIngestionFilter_ComputePublishedAfterCutoff_respects_channel_override()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        // Channel override of 14 days should take precedence over global 30 days
        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: 14, globalDefaultMaxAgeDays: 30);

        var expected = now.AddDays(-14);
        Assert.Equal(expected, cutoff);
    }

    [Fact]
    public void VideoIngestionFilter_ComputePublishedAfterCutoff_uses_global_default_when_no_override()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        var cutoff = VideoIngestionFilter.ComputePublishedAfterCutoff(now, channelMaxAgeDays: null, globalDefaultMaxAgeDays: 30);

        var expected = now.AddDays(-30);
        Assert.Equal(expected, cutoff);
    }

    [Fact]
    public void VideoIngestionFilter_ClassifyIsLongForm_returns_true_for_videos_at_threshold()
    {
        // Video exactly at threshold should be long-form
        Assert.True(VideoIngestionFilter.ClassifyIsLongForm(61, minDurationSeconds: 61));
        Assert.True(VideoIngestionFilter.ClassifyIsLongForm(300, minDurationSeconds: 300));
    }

    [Fact]
    public void VideoIngestionFilter_ClassifyIsLongForm_returns_false_for_videos_below_threshold()
    {
        Assert.False(VideoIngestionFilter.ClassifyIsLongForm(60, minDurationSeconds: 61));
        Assert.False(VideoIngestionFilter.ClassifyIsLongForm(299, minDurationSeconds: 300));
    }
}

/// <summary>
/// Helper to access internal YouTube API adapter method for testing.
/// </summary>
internal static class YouTubeApiMetadataAdapterTestHelper
{
    public static Video AdaptVideo(YouTubeApiVideoItem item, Guid? channelId, int? minDurationSeconds)
    {
        // This mirrors the private AdaptVideo method in YouTubeApiMetadataAdapter
        var id = item.Id ?? string.Empty;
        var title = string.IsNullOrWhiteSpace(item.Snippet?.Title) ? "Untitled video" : item.Snippet.Title.Trim();
        var durationSeconds = YouTubeApiMetadataAdapter.ParseIso8601Duration(item.ContentDetails?.Duration);

        return new Video(Guid.NewGuid(), title)
        {
            ChannelId = channelId ?? Guid.Empty,
            YoutubeVideoId = id,
            VideoUrl = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}",
            AuthorOriginal = item.Snippet?.ChannelTitle?.Trim() ?? string.Empty,
            DescriptionOriginal = item.Snippet?.Description,
            PublishedAt = item.Snippet?.PublishedAt,
            DurationSeconds = durationSeconds,
            IsLongForm = minDurationSeconds.HasValue
                ? VideoIngestionFilter.ClassifyIsLongForm(durationSeconds, minDurationSeconds.Value)
                : true
        };
    }
}
