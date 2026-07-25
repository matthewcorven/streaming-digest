namespace StreamingDigest.Domain;

public sealed class Notification : AuditedEntity
{
    public Guid Id { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? IngestionRunId { get; set; }
    public string NotificationType { get; set; } = "ingestion_summary";
    public string Provider { get; set; } = "matrix";
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? PayloadJson { get; set; }
    public string? RenderedBody { get; set; }
    public string? MessageSummary { get; set; }
    public string? ProviderMessageId { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
