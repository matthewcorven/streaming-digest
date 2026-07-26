using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Observability;

namespace StreamingDigest.Worker.Scraping;

public sealed class ScraperClient(
    HttpClient httpClient,
    IScrapeFailureRecorder scrapeFailureRecorder,
    WorkerOperationConcurrencyController concurrencyController,
    ApplicationConfiguration applicationConfiguration)
{
    public async Task<ScrapeFirstPageResponse> ScrapeFirstPageAsync(ScrapeFirstPageRequest request, CancellationToken cancellationToken = default)
    {
        return await CorrelationContext.RunWithActivityAsync(
            "scraper.scrape_first_page",
            async activity =>
            {
                activity?.SetTag("scraper.url", request.Url);
                activity?.SetTag("scraper.respect_robots_txt", request.RespectRobotsTxt.ToString());
                activity?.SetTag("scraper.debug_capture_raw_html", request.DebugCaptureRawHtml.ToString());
                activity?.SetTag("scraper.timeout_seconds", request.TimeoutSeconds.ToString());

                var effectiveRequest = BuildEffectiveRequest(request);
                activity?.SetTag("scraper.effective_respect_robots_txt", effectiveRequest.RespectRobotsTxt.ToString());
                activity?.SetTag("scraper.rate_limit_delay_ms", effectiveRequest.RateLimitDelayMs.ToString());

                try
                {
                    return await concurrencyController.RunWebsiteScrapeAsync(
                        request.Url,
                        async () =>
                        {
                            using var response = await httpClient.PostAsJsonAsync("/internal/scrape/first-page", effectiveRequest, cancellationToken);
                            response.EnsureSuccessStatusCode();
                            var payload = await response.Content.ReadFromJsonAsync<ScrapeFirstPageResponse>(cancellationToken: cancellationToken);
                            return payload ?? throw new InvalidOperationException("The scraper returned an empty payload.");
                        },
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await scrapeFailureRecorder.RecordFailureAsync(request, ex, cancellationToken);
                    throw;
                }
            },
            new Dictionary<string, object?>
            {
                ["scraper.url"] = request.Url,
                ["scraper.respect_robots_txt"] = request.RespectRobotsTxt,
                ["scraper.debug_capture_raw_html"] = request.DebugCaptureRawHtml,
                ["scraper.timeout_seconds"] = request.TimeoutSeconds
            },
            ActivityKind.Client);
    }

    private ScrapeFirstPageRequest BuildEffectiveRequest(ScrapeFirstPageRequest request)
    {
        var effectiveRespectRobotsTxt = request.RespectRobotsTxt;
        if (effectiveRespectRobotsTxt)
        {
            effectiveRespectRobotsTxt = ScrapingPolicyResolver.ShouldRespectRobotsTxt(request.Url, applicationConfiguration.Scraping);
        }

        return request with
        {
            RespectRobotsTxt = effectiveRespectRobotsTxt,
            RateLimitDelayMs = Math.Max(0, applicationConfiguration.Scraping.RateLimitDelayMs)
        };
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return await CorrelationContext.RunWithActivityAsync(
            "scraper.healthcheck",
            async activity =>
            {
                activity?.SetTag("scraper.target", "/health");

                try
                {
                    using var response = await httpClient.GetAsync("/health", cancellationToken);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            },
            new Dictionary<string, object?>
            {
                ["scraper.target"] = "/health"
            },
            ActivityKind.Client);
    }
}

public sealed record ScrapeFirstPageRequest(
    string Url,
    bool RespectRobotsTxt = true,
    bool DebugCaptureRawHtml = false,
    int TimeoutSeconds = 30,
    int RateLimitDelayMs = 1000);

public sealed record ScrapeFirstPageResponse(
    string RequestedUrl,
    string FinalUrl,
    string? Title,
    string? Description,
    JsonElement OpenGraph,
    string VisibleText,
    bool RobotsAllowed,
    int HttpStatus,
    string? ContentType,
    string ContentHash,
    string? RawHtmlDebugPath,
    string? ExclusionReason = null);
