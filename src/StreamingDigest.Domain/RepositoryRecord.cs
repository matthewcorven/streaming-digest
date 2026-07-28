namespace StreamingDigest.Domain;

/// <summary>Canonical repository metadata record. Named RepositoryRecord to avoid conflict with System.Reflection.Repository.</summary>
public sealed class RepositoryRecord : AuditedEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Host { get; set; } = string.Empty;
    public string CanonicalUrl { get; set; } = string.Empty;
    public string? Owner { get; set; }
    public string? Name { get; set; }
    public string? NormalizedOwner { get; set; }
    public string? NormalizedName { get; set; }
    public string? DefaultBranch { get; set; }
    public string? DescriptionOriginal { get; set; }
    public string? DescriptionOverride { get; set; }
    public int? Stars { get; set; }
    public int? Forks { get; set; }
    public string? PrimaryLanguage { get; set; }
    public string[]? Topics { get; set; }
    public string? LicenseSpdxId { get; set; }
    public string? DeepwikiUrl { get; set; }
    public DateTimeOffset? DeepwikiCheckedAt { get; set; }
    public string? RawMetadataJson { get; set; }
}
