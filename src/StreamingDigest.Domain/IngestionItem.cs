namespace StreamingDigest.Domain;

public sealed class IngestionItem
{
    public Guid Id { get; set; }
    public Guid IngestionRunId { get; set; }
    public Guid? OperationId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public Guid? ItemId { get; set; }
    public string? ExternalKey { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? DependsOnItemId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? StageVersion { get; set; }
    public string? JobPayloadVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public int RetryCount { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsRetryable { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? DeferredUntil { get; set; }
    public string? DefermentReason { get; set; }
    public string? WorkerId { get; set; }
    public string? StartedByJobId { get; set; }
    public string? CompletedByJobId { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
