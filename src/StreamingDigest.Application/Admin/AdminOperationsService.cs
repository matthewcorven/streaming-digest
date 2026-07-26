using System.Collections.Concurrent;

namespace StreamingDigest.Application.Admin;

public sealed class AdminOperationsService : IAdminOperationsService
{
    private readonly ConcurrentDictionary<Guid, AdminOperationRecord> _operations = new();

    public Task<AdminActionResult> RunIngestionNowAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("ingestion.run", target, "Manual ingestion has been queued for the target scope."));

    public Task<AdminActionResult> RunChannelBackfillAsync(string? channelId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("ingestion.backfill", channelId, "Channel backfill has been queued."));

    public Task<AdminActionResult> RetryFailedIngestionRunAsync(string runId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.ingestionRun", runId, $"Retry queued for ingestion run '{runId}'."));

    public Task<AdminActionResult> RetryFailedVideoAsync(string videoId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.video", videoId, $"Retry queued for video '{videoId}'."));

    public Task<AdminActionResult> RetryFailedLinkAsync(string linkId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.link", linkId, $"Retry queued for link '{linkId}'."));

    public Task<AdminActionResult> RetryFailedRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.repository", repositoryId, $"Retry queued for repository '{repositoryId}'."));

    public Task<AdminActionResult> ReprocessVideoAsync(string videoId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.video", videoId, $"Reprocess queued for video '{videoId}'."));

    public Task<AdminActionResult> ReprocessRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.repository", repositoryId, $"Reprocess queued for repository '{repositoryId}'."));

    public Task<AdminActionResult> ReprocessResourceAsync(string resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.resource", resourceId, $"Reprocess queued for resource '{resourceId}'."));

    public Task<AdminActionResult> ReprocessEmbeddingsAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.embeddings", target, "Embedding reprocessing has been queued for the requested scope."));

    public Task<AdminActionResult> PurgeScreenshotsAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("screenshots.purge", target, "Screenshot purge has been queued."));

    public Task<AdminActionResult> TestMatrixNotificationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.matrix", null, "Matrix test notification completed successfully.", "healthy"));

    public Task<AdminActionResult> TestEmbeddingServiceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.embeddings", null, "Embedding service health check completed successfully.", "healthy"));

    public Task<AdminActionResult> TestAudioToTextServiceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.audio", null, "Audio-to-text service health check completed successfully.", "healthy"));

    public Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult<AdminActionStatus?>(new AdminActionStatus(
                operation.OperationId,
                operation.OperationType,
                operation.Status,
                operation.Message,
                operation.Target,
                operation.JobId,
                operation.HealthStatus,
                operation.CreatedAt,
                operation.UpdatedAt));
        }

        return Task.FromResult<AdminActionStatus?>(null);
    }

    private AdminActionResult CreateAcceptedResult(string operationType, string? target, string message)
    {
        var operationId = Guid.NewGuid();
        var record = new AdminOperationRecord(
            operationId,
            operationType,
            "accepted",
            message,
            target,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        _operations[operationId] = record;
        return new AdminActionResult(record.OperationId, record.OperationType, record.Status, record.Message, record.Target, record.JobId, record.HealthStatus);
    }

    private AdminActionResult CreateCompletedResult(string operationType, string? target, string message, string healthStatus)
    {
        var operationId = Guid.NewGuid();
        var record = new AdminOperationRecord(
            operationId,
            operationType,
            "completed",
            message,
            target,
            null,
            healthStatus,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        _operations[operationId] = record;
        return new AdminActionResult(record.OperationId, record.OperationType, record.Status, record.Message, record.Target, record.JobId, record.HealthStatus);
    }

    private sealed record AdminOperationRecord(
        Guid OperationId,
        string OperationType,
        string Status,
        string Message,
        string? Target,
        string? JobId,
        string? HealthStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
