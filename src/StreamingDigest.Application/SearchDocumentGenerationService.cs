using System.Security.Cryptography;
using System.Text;

namespace StreamingDigest.Application;

public interface ISearchDocumentGenerationService
{
    IReadOnlyList<SearchDocument> Generate(IEnumerable<SearchDocumentCandidate> candidates);

    SearchDocument CreateDocument(SearchDocumentCandidate candidate);
}

public sealed class SearchDocumentGenerationService : ISearchDocumentGenerationService
{
    private readonly IEffectiveValueService _effectiveValueService;

    public SearchDocumentGenerationService() : this(new EffectiveValueService())
    {
    }

    public SearchDocumentGenerationService(IEffectiveValueService effectiveValueService)
    {
        _effectiveValueService = effectiveValueService ?? throw new ArgumentNullException(nameof(effectiveValueService));
    }

    public IReadOnlyList<SearchDocument> Generate(IEnumerable<SearchDocumentCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates.Select(CreateDocument).ToList();
    }

    public SearchDocument CreateDocument(SearchDocumentCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.IsNullOrWhiteSpace(candidate.DocumentType))
        {
            throw new ArgumentException("A document type is required.", nameof(candidate));
        }

        if (string.IsNullOrWhiteSpace(candidate.SourceEntityType))
        {
            throw new ArgumentException("A source entity type is required.", nameof(candidate));
        }

        var titleResolution = _effectiveValueService.Resolve(candidate.TitleOriginal, candidate.TitleOverride);
        var bodyResolution = _effectiveValueService.Resolve(candidate.BodyOriginal, candidate.BodyOverride);

        var titleEffective = titleResolution.Effective;
        var bodyEffective = bodyResolution.Effective;
        var contentHash = ComputeContentHash(titleEffective, bodyEffective);

        return new SearchDocument(
            Id: Guid.NewGuid(),
            DocumentType: candidate.DocumentType.Trim(),
            SourceEntityType: candidate.SourceEntityType.Trim(),
            SourceEntityId: candidate.SourceEntityId,
            SourceFieldName: candidate.SourceFieldName,
            ChunkIndex: candidate.ChunkIndex,
            ChunkStartOffset: candidate.ChunkStartOffset,
            ChunkEndOffset: candidate.ChunkEndOffset,
            TokenCount: candidate.TokenCount,
            SearchWeight: candidate.SearchWeight,
            EmbeddingRequired: candidate.EmbeddingRequired,
            ParentVideoId: candidate.ParentVideoId,
            ParentSegmentId: candidate.ParentSegmentId,
            TitleEffective: titleEffective,
            BodyEffective: bodyEffective,
            MetadataJson: candidate.MetadataJson,
            FtsWeightConfigJson: candidate.FtsWeightConfigJson,
            ContentHash: contentHash,
            IsStale: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static string ComputeContentHash(string? title, string? body)
    {
        var titleBytes = Encoding.UTF8.GetBytes(title ?? string.Empty);
        var bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);

        // Length-delimited encoding: [4-byte length][content][4-byte length][content]
        // Prevents cross-boundary collisions where title="foo\n" + body="bar"
        // would otherwise hash identically to title="foo" + body="\nbar".
        var buffer = new byte[4 + titleBytes.Length + 4 + bodyBytes.Length];
        var span = buffer.AsSpan();
        BitConverter.TryWriteBytes(span[..4], titleBytes.Length);
        titleBytes.CopyTo(span[4..]);
        BitConverter.TryWriteBytes(span[(4 + titleBytes.Length)..(8 + titleBytes.Length)], bodyBytes.Length);
        bodyBytes.CopyTo(span[(8 + titleBytes.Length)..]);

        return Convert.ToHexString(SHA256.HashData(buffer));
    }
}

public sealed record SearchDocumentCandidate(
    string DocumentType,
    string SourceEntityType,
    Guid SourceEntityId,
    Guid? ParentVideoId = null,
    string? SourceFieldName = null,
    int? ChunkIndex = null,
    int? ChunkStartOffset = null,
    int? ChunkEndOffset = null,
    int? TokenCount = null,
    decimal? SearchWeight = null,
    bool EmbeddingRequired = true,
    string? TitleOriginal = null,
    string? TitleOverride = null,
    string? BodyOriginal = null,
    string? BodyOverride = null,
    string? MetadataJson = null,
    string? FtsWeightConfigJson = null,
    Guid? ParentSegmentId = null);

public sealed record SearchDocument(
    Guid Id,
    string DocumentType,
    string SourceEntityType,
    Guid SourceEntityId,
    string? SourceFieldName,
    int? ChunkIndex,
    int? ChunkStartOffset,
    int? ChunkEndOffset,
    int? TokenCount,
    decimal? SearchWeight,
    bool EmbeddingRequired,
    Guid? ParentVideoId,
    Guid? ParentSegmentId,
    string? TitleEffective,
    string? BodyEffective,
    string? MetadataJson,
    string? FtsWeightConfigJson,
    string ContentHash,
    bool IsStale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
