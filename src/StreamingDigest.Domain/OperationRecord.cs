namespace StreamingDigest.Domain;

public sealed class OperationRecord
{
    public Guid Id { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RiskLevel { get; set; }
    public string? RequestedBy { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? SummaryJson { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
