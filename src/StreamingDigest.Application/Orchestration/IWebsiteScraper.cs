namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Port for the website-scraping client. Defined in Application so the orchestrator
/// stays UI/host-agnostic; the Worker host adapts its <c>ScraperClient</c> to this
/// contract. Mirrors <c>ScrapeFirstPageRequest/Response</c> from the Worker.
/// </summary>
public interface IWebsiteScraper
{
    Task<WebsiteScrapeResult> ScrapeFirstPageAsync(
        WebsiteScrapeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WebsiteScrapeRequest(
    string Url,
    Guid ExternalResourceId,
    bool RespectRobotsTxt = true,
    int TimeoutSeconds = 30,
    int RateLimitDelayMs = 1000);

public sealed record WebsiteScrapeResult(
    string RequestedUrl,
    string FinalUrl,
    string? Title,
    string? Description,
    string? OpenGraphJson,
    string? VisibleText,
    bool RobotsAllowed,
    int HttpStatus,
    string? ContentType,
    string? ContentHash,
    string? ExclusionReason = null);
