using StreamingDigest.Domain;

namespace StreamingDigest.MatrixNotifier;

public interface IMatrixNotificationService
{
    bool IsEnabled { get; }

    Task<MatrixSendResult> SendDigestSummaryAsync(Digest digest, CancellationToken cancellationToken = default);

    Task<MatrixSendResult> SendTestNotificationAsync(CancellationToken cancellationToken = default);
}

public sealed class MatrixNotificationService(MatrixNotificationClient client, MatrixNotificationOptions options) : IMatrixNotificationService
{
    public bool IsEnabled => options.IsEnabled;

    public async Task<MatrixSendResult> SendDigestSummaryAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);

        if (!options.IsEnabled)
        {
            return new MatrixSendResult(false, "Matrix notifications are disabled.");
        }

        if (!ShouldSendForRunType(digest))
        {
            return new MatrixSendResult(false, "Matrix notifications are disabled for this run type.");
        }

        var payload = DigestPayloadSerializer.Deserialize(digest.PayloadJson);
        var message = BuildDigestMessage(digest, payload);
        return await client.SendTextMessageAsync(message, cancellationToken);
    }

    public Task<MatrixSendResult> SendTestNotificationAsync(CancellationToken cancellationToken = default)
        => client.SendTestMessageAsync(cancellationToken);

    private string BuildDigestMessage(Digest digest, DigestPayload payload)
    {
        var sections = new List<string>();
        if (payload.NewVideos.Count > 0)
        {
            sections.Add($"{payload.NewVideos.Count} new video{Pluralize(payload.NewVideos.Count)}");
        }

        if (payload.NewResources.Count > 0)
        {
            sections.Add($"{payload.NewResources.Count} new resource{Pluralize(payload.NewResources.Count)}");
        }

        if (payload.HighSignalMatches.Count > 0)
        {
            sections.Add($"{payload.HighSignalMatches.Count} high-signal match{Pluralize(payload.HighSignalMatches.Count)}");
        }

        if (payload.FailedItems.Count > 0)
        {
            sections.Add($"{payload.FailedItems.Count} failed item{Pluralize(payload.FailedItems.Count)}");
        }

        if (payload.SkippedItems.Count > 0)
        {
            sections.Add($"{payload.SkippedItems.Count} skipped item{Pluralize(payload.SkippedItems.Count)}");
        }

        if (payload.ActiveDeferments.Count > 0)
        {
            sections.Add($"{payload.ActiveDeferments.Count} active deferral{Pluralize(payload.ActiveDeferments.Count)}");
        }

        if (sections.Count == 0)
        {
            sections.Add("no new items");
        }

        var summary = string.Join(", ", sections);
        var dashboardUrl = BuildDashboardUrl(digest);
        return $"Streaming Digest {digest.RunType} run {digest.IngestionRunId:N}: {summary}. Dashboard: {dashboardUrl}";
    }

    private bool ShouldSendForRunType(Digest digest)
    {
        var normalizedRunType = digest.RunType.Trim().ToLowerInvariant();
        return normalizedRunType switch
        {
            "manual" => options.OnManualRuns,
            "scheduled" => options.OnScheduledRuns,
            "backfill" => options.OnBackfillRuns,
            _ => options.OnScheduledRuns || options.OnManualRuns
        };
    }

    private string BuildDashboardUrl(Digest digest)
    {
        var baseUrl = options.DashboardBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/runs/{digest.IngestionRunId:N}";
    }

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";
}
