using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class LinkClassificationServiceTests
{
    private readonly ILinkClassificationService _service = new LinkClassificationService();

    [Fact]
    public void Classify_returns_code_repository_for_github_urls()
    {
        var result = _service.Classify("https://github.com/matthewcorven/streaming-digest");

        Assert.Equal(LinkClassification.CodeRepository, result.Classification);
        Assert.Equal("rule", result.Method);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public void Classify_returns_social_for_social_platform_urls()
    {
        var result = _service.Classify("https://x.com/streamingdigest");

        Assert.Equal(LinkClassification.Social, result.Classification);
    }

    [Fact]
    public void Classify_returns_newsletter_for_newsletter_hosts()
    {
        var result = _service.Classify("https://substack.com/p/hello-world");

        Assert.Equal(LinkClassification.Newsletter, result.Classification);
    }

    [Fact]
    public void Classify_returns_affiliate_for_affiliate_markers()
    {
        var result = _service.Classify("https://amzn.to/4abc123");

        Assert.Equal(LinkClassification.Affiliate, result.Classification);
    }

    [Fact]
    public void Classify_returns_ad_sponsor_for_sponsor_markers()
    {
        var result = _service.Classify("https://example.com/sponsored/launch");

        Assert.Equal(LinkClassification.AdSponsor, result.Classification);
    }

    [Fact]
    public void Classify_returns_course_for_course_urls()
    {
        var result = _service.Classify("https://www.udemy.com/course/ai-fundamentals");

        Assert.Equal(LinkClassification.Course, result.Classification);
    }

    [Fact]
    public void Classify_returns_merch_for_merchandise_urls()
    {
        var result = _service.Classify("https://www.etsy.com/shop/example");

        Assert.Equal(LinkClassification.Merch, result.Classification);
    }

    [Fact]
    public void Classify_returns_website_resource_for_general_web_urls()
    {
        var result = _service.Classify("https://docs.example.com/guide");

        Assert.Equal(LinkClassification.WebsiteResource, result.Classification);
    }

    [Fact]
    public void Classify_returns_unknown_for_blank_input()
    {
        var result = _service.Classify("   ");

        Assert.Equal(LinkClassification.Unknown, result.Classification);
    }

    [Fact]
    public void Classify_returns_other_for_non_web_urls()
    {
        var result = _service.Classify("mailto:test@example.com");

        Assert.Equal(LinkClassification.Other, result.Classification);
    }
}
