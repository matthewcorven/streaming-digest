namespace StreamingDigest.Application.Models;

public sealed class ModelRuntimeState
{
    public required Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public required string RuntimeRole { get; set; }
    public required string Status { get; set; }
    public Guid? CurrentOperationId { get; set; }
    public int? ProgressPercent { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? LastSeenInRuntimeAt { get; set; }
    public string? LastErrorSummary { get; set; }
    public string? DetailsJson { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
}
