using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Observability;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Worker.Scraping;

public sealed class ScraperClient(
    HttpClient httpClient,
    IScrapeFailureRecorder scrapeFailureRecorder,
    WorkerOperationConcurrencyController concurrencyController,
    ApplicationConfiguration applicationConfiguration,
    StreamingDigestDbContext? context = null)
{
    public async Task<ScrapeFirstPageResponse> ScrapeFirstPageAsync(ScrapeFirstPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExternalResourceId == Guid.Empty)
        {
            throw new ArgumentException("An external resource identifier is required.", nameof(request));
        }

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
                            var scrapeResult = payload ?? throw new InvalidOperationException("The scraper returned an empty payload.");
                            if (context is not null)
                            {
                                context.ScrapedPages.Add(MapToScrapedPage(request.ExternalResourceId, scrapeResult));
                                await context.SaveChangesAsync(cancellationToken);
                            }

                            return scrapeResult;
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

    private static ScrapedPage MapToScrapedPage(Guid externalResourceId, ScrapeFirstPageResponse response)
    {
        return new ScrapedPage
        {
            ExternalResourceId = externalResourceId,
            FinalUrl = string.IsNullOrWhiteSpace(response.FinalUrl) ? response.RequestedUrl : response.FinalUrl,
            TitleOriginal = response.Title,
            DescriptionOriginal = response.Description,
            OpenGraphJson = response.OpenGraph.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : response.OpenGraph.GetRawText(),
            VisibleTextOriginal = response.VisibleText,
            RobotsAllowed = response.RobotsAllowed,
            ScrapeStatus = string.IsNullOrWhiteSpace(response.ExclusionReason) ? "succeeded" : "excluded",
            ExclusionReason = response.ExclusionReason,
            HttpStatus = response.HttpStatus,
            ContentType = response.ContentType,
            ContentHash = response.ContentHash,
            RawHtmlDebugPath = response.RawHtmlDebugPath,
            ScrapedAt = DateTimeOffset.UtcNow,
            ErrorSummary = response.ExclusionReason
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
    Guid ExternalResourceId,
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
