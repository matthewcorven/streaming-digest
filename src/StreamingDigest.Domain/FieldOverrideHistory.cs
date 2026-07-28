namespace StreamingDigest.Domain;

public sealed class FieldOverrideHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string FieldName { get; init; } = string.Empty;
    public string? PreviousValue { get; init; }
    public string? NewValue { get; init; }
    public DateTimeOffset ChangedAt { get; init; } = DateTimeOffset.UtcNow;
}
