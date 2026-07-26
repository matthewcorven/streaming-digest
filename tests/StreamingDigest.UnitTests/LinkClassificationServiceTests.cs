using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [InlineData("https://gitlab.com/example/project")]
    [InlineData("https://bitbucket.org/example/project")]
    public void Classify_returns_code_repository_for_supported_repository_hosts(string url)
    {
        var result = _service.Classify(url);

        Assert.Equal(LinkClassification.CodeRepository, result.Classification);
        Assert.Equal("rule", result.Method);
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

    [Fact]
    public void Classify_uses_llm_result_when_available()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":{\"content\":\"{\\\"classification\\\":\\\"Course\\\",\\\"confidence\\\":0.92}\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };

        var service = new LinkClassificationService(httpClient);
        var result = service.Classify("https://www.udemy.com/course/ai-fundamentals");

        Assert.Equal(LinkClassification.Course, result.Classification);
        Assert.Equal("llm", result.Method);
        Assert.Equal(0.92, result.Confidence, 5);
    }

    [Fact]
    public void Classify_includes_examples_in_llm_prompt()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("https://example.com/sponsor", body);
            Assert.Contains("AdSponsor", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":{\"content\":\"{\\\"classification\\\":\\\"WebsiteResource\\\",\\\"confidence\\\":0.88}\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new LinkClassificationService(httpClient);
        var examples = new[]
        {
            new LinkClassificationExample("https://example.com/sponsor", LinkClassification.AdSponsor, "sponsored content")
        };

        var result = service.Classify("https://example.com/guide", examples);

        Assert.Equal(LinkClassification.WebsiteResource, result.Classification);
        Assert.Equal("llm", result.Method);
    }

    [Fact]
    public void Classify_uses_active_corrections_as_llm_examples()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("https://example.com/corrected", body);
            Assert.Contains("Affiliate", body);
            Assert.DoesNotContain("https://example.com/inactive", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":{\"content\":\"{\\\"classification\\\":\\\"WebsiteResource\\\",\\\"confidence\\\":0.88}\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new LinkClassificationService(httpClient);
        var corrections = new[]
        {
            new LinkClassificationCorrection("https://example.com/corrected", LinkClassification.WebsiteResource, LinkClassification.Affiliate, "affiliate content", true),
            new LinkClassificationCorrection("https://example.com/inactive", LinkClassification.WebsiteResource, LinkClassification.AdSponsor, "inactive correction", false)
        };

        var result = service.Classify("https://example.com/guide", corrections);

        Assert.Equal(LinkClassification.WebsiteResource, result.Classification);
        Assert.Equal("llm", result.Method);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}
