namespace StreamingDigest.UnitTests;

using StreamingDigest.Application;
using StreamingDigest.Domain;

public sealed class VideoUnavailableStateServiceTests
{
    [Fact]
    public void MarkUnavailable_sets_ingestion_status_to_unavailable()
    {
        var video = MakeVideo();

        VideoUnavailableStateService.MarkUnavailable(video, DateTimeOffset.UtcNow);

        Assert.Equal(IngestionStatuses.Unavailable, video.IngestionStatus);
    }

    [Fact]
    public void MarkUnavailable_sets_metadata_fetched_at_to_now()
    {
        var video = MakeVideo();
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        VideoUnavailableStateService.MarkUnavailable(video, now);

        Assert.Equal(now, video.MetadataFetchedAt);
    }

    [Fact]
    public void MarkUnavailable_overwrites_previous_status()
    {
        var failedVideo = MakeVideo(ingestionStatus: IngestionStatuses.Failed);
        var processingVideo = MakeVideo(ingestionStatus: IngestionStatuses.Processing);

        VideoUnavailableStateService.MarkUnavailable(failedVideo, DateTimeOffset.UtcNow);
        VideoUnavailableStateService.MarkUnavailable(processingVideo, DateTimeOffset.UtcNow);

        Assert.Equal(IngestionStatuses.Unavailable, failedVideo.IngestionStatus);
        Assert.Equal(IngestionStatuses.Unavailable, processingVideo.IngestionStatus);
    }

    [Fact]
    public void MarkUnavailable_is_idempotent()
    {
        var video = MakeVideo();
        var first = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(1);

        VideoUnavailableStateService.MarkUnavailable(video, first);
        VideoUnavailableStateService.MarkUnavailable(video, second);

        Assert.Equal(IngestionStatuses.Unavailable, video.IngestionStatus);
        Assert.Equal(second, video.MetadataFetchedAt);
    }

    [Fact]
    public void ShouldSkip_returns_true_for_unavailable_status()
    {
        Assert.True(VideoUnavailableStateService.ShouldSkip(IngestionStatuses.Unavailable));
    }

    [Fact]
    public void ShouldSkip_returns_false_for_non_unavailable_status()
    {
        Assert.False(VideoUnavailableStateService.ShouldSkip(IngestionStatuses.Pending));
        Assert.False(VideoUnavailableStateService.ShouldSkip(null));
    }

    private static Video MakeVideo(string ingestionStatus = IngestionStatuses.Pending)
        => new(Guid.NewGuid(), "Test Video")
        {
            ChannelId = Guid.NewGuid(),
            PlatformVideoId = "video-123",
            YoutubeVideoId = "video-123",
            VideoUrl = "https://www.youtube.com/watch?v=video-123",
            PlatformVideoUrl = "https://www.youtube.com/watch?v=video-123",
            Title = "Test Video",
            AuthorOriginal = "Test Author",
            IngestionStatus = ingestionStatus
        };
}
