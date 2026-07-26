using StreamingDigest.Domain;

namespace StreamingDigest.Worker.Scraping;

public interface IScrapeFailureRecorder
{
    Task RecordFailureAsync(ScrapeFirstPageRequest request, Exception exception, CancellationToken cancellationToken = default);
}
