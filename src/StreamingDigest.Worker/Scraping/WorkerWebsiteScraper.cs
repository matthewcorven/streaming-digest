using System.Text.Json;
using StreamingDigest.Application.Orchestration;

namespace StreamingDigest.Worker.Scraping;

/// <summary>
/// Adapts the Worker's <see cref="ScraperClient"/> to the Application-level
/// <see cref="IWebsiteScraper"/> port so the A2 websites stage stays host-agnostic.
/// </summary>
/// <remarks>
/// <see cref="ScraperClient"/> already persists its own <c>ScrapedPage</c> row when it is
/// constructed with a DbContext (the Worker registers it that way); the stage's
/// <c>IVideoPipelinePersistence</c> skips pages for resources that already have a persisted
/// row to avoid double-writing.
/// </remarks>
public sealed class WorkerWebsiteScraper(ScraperClient scraperClient) : IWebsiteScraper
{
    /// <inheritdoc />
    public async Task<WebsiteScrapeResult> ScrapeFirstPageAsync(
        WebsiteScrapeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await scraperClient.ScrapeFirstPageAsync(
            new ScrapeFirstPageRequest(
                request.Url,
                request.ExternalResourceId,
                request.RespectRobotsTxt,
                DebugCaptureRawHtml: false,
                request.TimeoutSeconds,
                request.RateLimitDelayMs),
            cancellationToken);

        return new WebsiteScrapeResult(
            response.RequestedUrl,
            response.FinalUrl,
            response.Title,
            response.Description,
            response.OpenGraph.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : response.OpenGraph.GetRawText(),
            response.VisibleText,
            response.RobotsAllowed,
            response.HttpStatus,
            response.ContentType,
            response.ContentHash,
            response.ExclusionReason);
    }
}
