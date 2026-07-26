namespace StreamingDigest.Application.Admin;

public interface IAdminOperationsService
{
    Task<AdminActionResult> RunIngestionNowAsync(string? target = null, CancellationToken cancellationToken = default);
    Task<AdminActionResult> RunChannelBackfillAsync(string? channelId = null, CancellationToken cancellationToken = default);
    Task<AdminActionResult> RetryFailedIngestionRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> RetryFailedVideoAsync(string videoId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> RetryFailedLinkAsync(string linkId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> RetryFailedRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> ReprocessVideoAsync(string videoId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> ReprocessRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> ReprocessResourceAsync(string resourceId, CancellationToken cancellationToken = default);
    Task<AdminActionResult> ReprocessEmbeddingsAsync(string? target = null, CancellationToken cancellationToken = default);
    Task<AdminActionResult> PurgeScreenshotsAsync(string? target = null, CancellationToken cancellationToken = default);
    Task<AdminActionResult> TestMatrixNotificationAsync(CancellationToken cancellationToken = default);
    Task<AdminActionResult> TestEmbeddingServiceAsync(CancellationToken cancellationToken = default);
    Task<AdminActionResult> TestAudioToTextServiceAsync(CancellationToken cancellationToken = default);
    Task<AdminActionResult> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task<AdminActionResult> RestoreLatestBackupAsync(CancellationToken cancellationToken = default);
    Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default);
}

public sealed record AdminActionResult(
    Guid OperationId,
    string OperationType,
    string Status,
    string Message,
    string? Target = null,
    string? JobId = null,
    string? HealthStatus = null);

public sealed record AdminActionStatus(
    Guid OperationId,
    string OperationType,
    string Status,
    string Message,
    string? Target,
    string? JobId,
    string? HealthStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IAdminOperationStore
{
    Task PersistOperationAsync(AdminActionStatus operation, CancellationToken cancellationToken = default);
    Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default);
}
