namespace StreamingDigest.Domain;

public sealed class ExternalResource : AuditedEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CanonicalUrl { get; set; } = string.Empty;
    public string? FinalUrl { get; set; }
    public string? Domain { get; set; }
    public string ResourceType { get; set; } = "unknown";
    public string? TitleOriginal { get; set; }
    public string? TitleOverride { get; set; }
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public string ClassificationOriginal { get; set; } = "unknown";
    public string? ClassificationOverride { get; set; }
    public decimal? ClassificationConfidence { get; set; }
    public string? ClassificationMethod { get; set; }
    public bool IsAdOrSponsor { get; set; }
    public string? RawMetadataJson { get; set; }
}
