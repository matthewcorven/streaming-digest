namespace StreamingDigest.Domain;

public abstract class AuditedEntity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[]? RowVersion { get; set; }
}
