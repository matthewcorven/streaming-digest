using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class VideoLinkExtractionServiceTests
{
    private readonly IVideoLinkExtractionService _service = new VideoLinkExtractionService();

    [Fact]
    public void Extract_returns_links_from_description_text()
    {
        var links = _service.Extract("See https://example.com/docs and https://example.org", null);

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("https://example.com/docs", link.Url);
                Assert.Equal(VideoLinkSource.Description, link.Source);
            },
            link =>
            {
                Assert.Equal("https://example.org", link.Url);
                Assert.Equal(VideoLinkSource.Description, link.Source);
            });
    }

    [Fact]
    public void Extract_returns_links_from_pinned_comment_text()
    {
        var links = _service.Extract(null, "Pinned comment: https://pinned.example.com and [repo](https://github.com/org/repo)");

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("https://pinned.example.com", link.Url);
                Assert.Equal(VideoLinkSource.PinnedComment, link.Source);
            },
            link =>
            {
                Assert.Equal("https://github.com/org/repo", link.Url);
                Assert.Equal(VideoLinkSource.PinnedComment, link.Source);
            });
    }

    [Fact]
    public void Extract_supports_markdown_links()
    {
        var links = _service.Extract("Docs: [guide](https://docs.example.com) and [api](https://api.example.com)", null);

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("https://docs.example.com", link.Url);
                Assert.Equal(VideoLinkSource.Description, link.Source);
            },
            link =>
            {
                Assert.Equal("https://api.example.com", link.Url);
                Assert.Equal(VideoLinkSource.Description, link.Source);
            });
    }

    [Fact]
    public void Extract_deduplicates_and_normalizes_links_within_the_same_source()
    {
        var links = _service.Extract("See https://example.com, https://example.com., and <https://example.com>", null);

        var singleLink = Assert.Single(links);
        Assert.Equal("https://example.com", singleLink.Url);
        Assert.Equal(VideoLinkSource.Description, singleLink.Source);
    }

    [Fact]
    public void Extract_returns_empty_collection_for_empty_input()
    {
        var links = _service.Extract(string.Empty, "   ");

        Assert.Empty(links);
    }
}
