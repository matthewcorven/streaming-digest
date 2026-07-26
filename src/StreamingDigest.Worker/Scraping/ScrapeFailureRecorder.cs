using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Worker.Scraping;

public sealed class ScrapeFailureRecorder(
    StreamingDigestDbContext context,
    ILogger<ScrapeFailureRecorder> logger) : IScrapeFailureRecorder
{
    public async Task RecordFailureAsync(ScrapeFirstPageRequest request, Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exception);

        var details = new
        {
            request.Url,
            request.RespectRobotsTxt,
            request.DebugCaptureRawHtml,
            request.TimeoutSeconds,
            request.RateLimitDelayMs,
            ExceptionType = exception.GetType().FullName,
            exception.Message
        };

        var domainEvent = new DomainEvent
        {
            EventType = DomainEventTypeCatalog.RequireDefined(DomainEventTypeCatalog.ScrapeFailed),
            Severity = "error",
            EntityType = "scrape_request",
            Message = $"Scrape failed for {request.Url}",
            DetailsJson = JsonSerializer.Serialize(details)
        };

        context.DomainEvents.Add(domainEvent);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogError(exception, "Scrape failed for {Url}", request.Url);
    }
}
