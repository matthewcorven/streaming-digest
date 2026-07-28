namespace StreamingDigest.Domain;

public sealed class Note : AuditedEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string? Title { get; set; }
    public string Markdown { get; set; } = string.Empty;
    public string EmbeddingStatus { get; set; } = "stale";
    public DateTimeOffset? DeletedAt { get; set; }
}
