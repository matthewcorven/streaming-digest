namespace StreamingDigest.Domain;

public sealed class DomainEvent : AuditedEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? IngestionRunId { get; set; }
    public Guid? OperationId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
}
