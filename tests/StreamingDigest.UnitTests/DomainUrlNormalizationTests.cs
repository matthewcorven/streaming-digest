using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class DomainUrlNormalizationTests
{
    [Fact]
    public void Channel_normalizes_profile_and_source_urls()
    {
        var channel = new Channel
        {
            YoutubeChannelId = " UC123456 ",
            ProfileUrl = " HTTPS://YouTube.com/@TestChannel/?utm_source=feed ",
            SourceUrl = "https://m.youtube.com/c/not-used?view=videos"
        };

        Assert.Equal("UC123456", channel.YoutubeChannelId);
        Assert.Equal("https://www.youtube.com/@TestChannel", channel.ProfileUrl);
        Assert.Equal("https://www.youtube.com/channel/UC123456", channel.SourceUrl);
    }

    [Fact]
    public void Video_normalizes_youtube_urls_to_canonical_forms()
    {
        var video = new Video(Guid.NewGuid(), "Example")
        {
            Platform = " youtube ",
            PlatformVideoId = " abc123 ",
            YoutubeVideoId = " abc123 ",
            PlatformVideoUrl = "https://youtu.be/abc123?si=share",
            AuthorOriginal = "Example author",
            VideoUrl = "https://m.youtube.com/watch?v=abc123&feature=share&utm_campaign=test",
            ThumbnailUrl = "https://img.youtube.com/vi/abc123/hqdefault.jpg?utm_source=test#fragment"
        };

        Assert.Equal("youtube", video.Platform);
        Assert.Equal("abc123", video.PlatformVideoId);
        Assert.Equal("abc123", video.YoutubeVideoId);
        Assert.Equal("https://www.youtube.com/watch", video.PlatformVideoUrl);
        Assert.Equal("https://www.youtube.com/watch?v=abc123", video.VideoUrl);
        Assert.Equal("https://img.youtube.com/vi/abc123/hqdefault.jpg", video.ThumbnailUrl);
    }

    [Fact]
    public void Video_preserves_non_tracking_query_parameters_for_non_youtube_urls()
    {
        var video = new Video(Guid.NewGuid(), "Example")
        {
            Platform = "vimeo",
            PlatformVideoUrl = "https://Example.com/video?id=123&utm_source=newsletter#details",
            PlatformVideoId = "123",
            AuthorOriginal = "Example author",
            VideoUrl = "https://Example.com/video?id=123&fbclid=tracking"
        };

        Assert.Equal("https://example.com/video?id=123", video.PlatformVideoUrl);
        Assert.Equal("https://example.com/video?id=123", video.VideoUrl);
    }
}
