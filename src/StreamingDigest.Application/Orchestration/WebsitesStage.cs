using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Stage 6 — website scraping. Scrapes the first page of each website-classified
/// external resource via <see cref="IWebsiteScraper"/>, honoring robots policy.
/// Robots exclusions and per-page failures are recorded on the
/// <see cref="ScrapedPage"/> row and degrade to warnings; they never fail the video.
/// </summary>
public sealed class WebsitesStageHandler(
    IWebsiteScraper websiteScraper,
    ILogger<WebsitesStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Websites;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        var websiteResources = context.Resources
            .Where(r => r.ResourceType is "website" or "newsletter" or "course")
            .ToList();

        foreach (var resource in websiteResources)
        {
            WebsiteScrapeResult result;
            try
            {
                result = await websiteScraper.ScrapeFirstPageAsync(
                    new WebsiteScrapeRequest(resource.CanonicalUrl, resource.Id),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Warnings.Add($"websites: scrape failed for {resource.CanonicalUrl}: {ex.Message}");
                context.ScrapedPages.Add(new ScrapedPage
                {
                    ExternalResourceId = resource.Id,
                    FinalUrl = resource.CanonicalUrl,
                    ScrapeStatus = "failed",
                    ErrorSummary = ex.Message,
                    ScrapedAt = DateTimeOffset.UtcNow,
                });
                context.PendingEvents.Add(new DomainEvent
                {
                    EventType = DomainEventTypeCatalog.ScrapeFailed,
                    Severity = "warning",
                    EntityType = "external_resource",
                    EntityId = resource.Id,
                    IngestionRunId = context.Run.Id,
                    Message = $"Scrape failed for '{resource.CanonicalUrl}': {ex.Message}",
                });
                continue;
            }

            var excluded = !string.IsNullOrWhiteSpace(result.ExclusionReason);
            var page = new ScrapedPage
            {
                ExternalResourceId = resource.Id,
                FinalUrl = result.FinalUrl,
                TitleOriginal = result.Title,
                DescriptionOriginal = result.Description,
                OpenGraphJson = result.OpenGraphJson,
                VisibleTextOriginal = result.VisibleText,
                RobotsAllowed = result.RobotsAllowed,
                ScrapeStatus = excluded ? "excluded" : "succeeded",
                ExclusionReason = result.ExclusionReason,
                HttpStatus = result.HttpStatus,
                ContentType = result.ContentType,
                ContentHash = result.ContentHash,
                ScrapedAt = DateTimeOffset.UtcNow,
            };
            context.ScrapedPages.Add(page);

            // Enrich the resource with scraped metadata for search documents.
            resource.FinalUrl ??= result.FinalUrl;
            resource.TitleOriginal ??= result.Title;
            resource.DescriptionOriginal ??= result.Description;

            if (excluded)
            {
                context.Warnings.Add($"websites: {resource.CanonicalUrl} excluded ({result.ExclusionReason})");
                context.PendingEvents.Add(new DomainEvent
                {
                    EventType = DomainEventTypeCatalog.ScrapeExcluded,
                    Severity = "info",
                    EntityType = "external_resource",
                    EntityId = resource.Id,
                    IngestionRunId = context.Run.Id,
                    Message = $"Scrape excluded for '{resource.CanonicalUrl}': {result.ExclusionReason}",
                });
            }

            logger.LogDebug(
                "Video {VideoId}: scraped {Url} -> {Status}",
                context.Video.YoutubeVideoId, resource.CanonicalUrl, page.ScrapeStatus);
        }
    }
}
