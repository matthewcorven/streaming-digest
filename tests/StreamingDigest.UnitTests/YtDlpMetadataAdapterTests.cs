using System.Text.Json;
using StreamingDigest.Application;
using StreamingDigest.UnitTests.Fixtures;

namespace StreamingDigest.UnitTests;

public sealed class YtDlpMetadataAdapterTests
{
    private readonly FixtureLoader _fixtures = new();
    private readonly YtDlpMetadataAdapter _adapter = new();

    // ── AdaptVideoMetadata — basic parsing ────────────────────────────────────

    [Fact]
    public void AdaptVideoMetadata_parses_title_and_id_from_fixture()
    {
        var payload = _fixtures.ReadText("ytdlp/video-metadata.json");

        var video = _adapter.AdaptVideoMetadata(payload);

        Assert.Equal("Fixture video", video.Title);
        Assert.Equal("fixture-video-id", video.YoutubeVideoId);
        Assert.Equal("Fixture Uploader", video.AuthorOriginal);
        Assert.Equal(84, video.DurationSeconds);
    }

    [Fact]
    public void AdaptVideoMetadata_returns_untitled_when_title_is_missing()
    {
        var video = _adapter.AdaptVideoMetadata("""{"id":"abc"}""");
        Assert.Equal("Untitled video", video.Title);
    }

    [Fact]
    public void AdaptVideoMetadata_returns_empty_video_when_payload_is_null()
    {
        var video = _adapter.AdaptVideoMetadata(null);
        Assert.Equal("Untitled video", video.Title);
        Assert.Equal(string.Empty, video.YoutubeVideoId);
    }

    // ── AdaptVideoMetadata — IsLongForm ───────────────────────────────────────

    [Fact]
    public void AdaptVideoMetadata_defaults_IsLongForm_to_true_when_no_minDuration_provided()
    {
        var payload = _fixtures.ReadText("ytdlp/video-metadata.json"); // duration: 84s
        var video = _adapter.AdaptVideoMetadata(payload, minDurationSeconds: null);
        Assert.True(video.IsLongForm);
    }

    [Fact]
    public void AdaptVideoMetadata_sets_IsLongForm_true_when_duration_meets_threshold()
    {
        var payload = _fixtures.ReadText("ytdlp/video-metadata.json"); // duration: 84s
        var video = _adapter.AdaptVideoMetadata(payload, minDurationSeconds: 61);
        Assert.True(video.IsLongForm); // 84s >= 61s
    }

    [Fact]
    public void AdaptVideoMetadata_sets_IsLongForm_false_when_duration_is_below_threshold()
    {
        var payload = _fixtures.ReadText("ytdlp/video-metadata.json"); // duration: 84s
        var video = _adapter.AdaptVideoMetadata(payload, minDurationSeconds: 85);
        Assert.False(video.IsLongForm); // 84s < 85s
    }

    [Fact]
    public void AdaptVideoMetadata_sets_IsLongForm_true_when_duration_is_null()
    {
        // Payload with no duration field
        var payload = """{"id":"abc","title":"Unknown Duration Video"}""";
        var video = _adapter.AdaptVideoMetadata(payload, minDurationSeconds: 61);
        Assert.True(video.IsLongForm); // null duration → assume long-form
    }

    [Fact]
    public void AdaptVideoMetadata_sets_IsLongForm_true_when_duration_equals_threshold()
    {
        var payload = """{"id":"abc","title":"Exactly At Threshold","duration":61}""";
        var video = _adapter.AdaptVideoMetadata(payload, minDurationSeconds: 61);
        Assert.True(video.IsLongForm); // 61s >= 61s (inclusive)
    }

    // ── AdaptVideoMetadata — published date ───────────────────────────────────

    [Fact]
    public void AdaptVideoMetadata_parses_published_at_from_upload_date()
    {
        var payload = _fixtures.ReadText("ytdlp/video-metadata.json"); // upload_date: 20260727
        var video = _adapter.AdaptVideoMetadata(payload);

        Assert.NotNull(video.PublishedAt);
        Assert.Equal(2026, video.PublishedAt!.Value.Year);
        Assert.Equal(7, video.PublishedAt.Value.Month);
        Assert.Equal(27, video.PublishedAt.Value.Day);
    }

    // ── AdaptChannelMetadata — basic parsing ──────────────────────────────────

    [Fact]
    public void AdaptChannelMetadata_parses_id_and_title_from_fixture()
    {
        var payload = _fixtures.ReadText("ytdlp/channel-metadata.json");
        var channel = _adapter.AdaptChannelMetadata(payload);

        Assert.NotEmpty(channel.YoutubeChannelId);
        Assert.NotEmpty(channel.NameOriginal);
    }

    [Fact]
    public void AdaptChannelMetadata_returns_untitled_when_title_is_missing()
    {
        var channel = _adapter.AdaptChannelMetadata("""{"id":"UC123"}""");
        Assert.Equal("Untitled channel", channel.NameOriginal);
    }

    // ── ParsePublishedAt ──────────────────────────────────────────────────────

    [Fact]
    public void ParsePublishedAt_prefers_upload_date_when_all_sources_present()
    {
        var result = YtDlpMetadataAdapter.ParsePublishedAt("20260101", releaseTimestamp: 9999999999L, timestamp: 8888888888L);
        Assert.NotNull(result);
        Assert.Equal(2026, result!.Value.Year);
        Assert.Equal(1, result.Value.Month);
        Assert.Equal(1, result.Value.Day);
    }

    [Fact]
    public void ParsePublishedAt_falls_back_to_release_timestamp_when_upload_date_absent()
    {
        var result = YtDlpMetadataAdapter.ParsePublishedAt(null, releaseTimestamp: 1753632000L, timestamp: null);
        Assert.NotNull(result);
        Assert.Equal(2025, result!.Value.Year); // 2025-07-27
    }

    [Fact]
    public void ParsePublishedAt_falls_back_to_timestamp_when_other_sources_absent()
    {
        var result = YtDlpMetadataAdapter.ParsePublishedAt(null, releaseTimestamp: null, timestamp: 1753632000L);
        Assert.NotNull(result);
        Assert.Equal(2025, result!.Value.Year);
    }

    [Fact]
    public void ParsePublishedAt_returns_null_when_all_sources_absent()
    {
        var result = YtDlpMetadataAdapter.ParsePublishedAt(null, releaseTimestamp: null, timestamp: null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026/07/27")]
    public void ParsePublishedAt_returns_null_for_invalid_upload_date_format(string uploadDate)
    {
        var result = YtDlpMetadataAdapter.ParsePublishedAt(uploadDate, releaseTimestamp: null, timestamp: null);
        Assert.Null(result);
    }
}
