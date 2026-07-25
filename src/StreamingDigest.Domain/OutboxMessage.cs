namespace StreamingDigest.Domain;

public sealed class OutboxMessage : AuditedEntity
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string? AggregateType { get; set; }
    public Guid? AggregateId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
