using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Worker.Scraping;

namespace StreamingDigest.UnitTests;

public sealed class ScraperClientTests
{
    [Fact]
    public async Task ScrapeFirstPageAsync_uses_domain_override_before_posting_to_scraper()
    {
        ScrapeFirstPageRequest? capturedRequest = null;
        string? requestedPath = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            capturedRequest = await request.Content!.ReadFromJsonAsync<ScrapeFirstPageRequest>(cancellationToken: cancellationToken);

            var payload = new ScrapeFirstPageResponse(
                "https://www.example.com/article",
                "https://www.example.com/target",
                null,
                null,
                JsonDocument.Parse("{}" ).RootElement,
                string.Empty,
                true,
                200,
                "text/html",
                "sha256:test",
                null);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://scraper.internal")
        };

        var configuration = new ApplicationConfiguration
        {
            Scraping = new ScrapingSettings
            {
                RespectRobotsTxtByDefault = true,
                RateLimitDelayMs = 250,
                DomainOverrides =
                [
                    new ScrapingDomainOverride { Domain = "example.com", RespectRobotsTxt = false }
                ]
            }
        };

        var client = new ScraperClient(
            httpClient,
            new NoOpScrapeFailureRecorder(),
            new WorkerOperationConcurrencyController(new WorkerConcurrencySettings()),
            configuration);

        var response = await client.ScrapeFirstPageAsync(new ScrapeFirstPageRequest("https://www.example.com/article", Guid.NewGuid(), RespectRobotsTxt: true));

        Assert.NotNull(capturedRequest);
        Assert.Equal("https://www.example.com/article", capturedRequest.Url);
        Assert.False(capturedRequest.RespectRobotsTxt);
        Assert.Equal(250, capturedRequest.RateLimitDelayMs);
        Assert.Equal("/internal/scrape/first-page", requestedPath);
        Assert.Equal("https://www.example.com/target", response.FinalUrl);
        Assert.Equal("https://www.example.com/article", response.RequestedUrl);
    }

    [Fact]
    public async Task ScrapeFirstPageAsync_preserves_robots_exclusion_details_from_scraper_response()
    {
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var payload = new ScrapeFirstPageResponse(
                "https://www.example.com/blocked",
                "https://www.example.com/blocked",
                null,
                null,
                JsonDocument.Parse("{}" ).RootElement,
                string.Empty,
                false,
                0,
                null,
                "sha256:blocked",
                null,
                "robots-txt");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://scraper.internal")
        };

        var client = new ScraperClient(
            httpClient,
            new NoOpScrapeFailureRecorder(),
            new WorkerOperationConcurrencyController(new WorkerConcurrencySettings()),
            new ApplicationConfiguration());

        var response = await client.ScrapeFirstPageAsync(new ScrapeFirstPageRequest("https://www.example.com/blocked", Guid.NewGuid(), RespectRobotsTxt: true));

        Assert.False(response.RobotsAllowed);
        Assert.Equal("https://www.example.com/blocked", response.RequestedUrl);
        Assert.Equal("robots-txt", response.ExclusionReason);
        Assert.Equal(string.Empty, response.VisibleText);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class NoOpScrapeFailureRecorder : IScrapeFailureRecorder
    {
        public Task RecordFailureAsync(ScrapeFirstPageRequest request, Exception exception, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
