namespace StreamingDigest.Application;

/// <summary>
/// Probes and queries the live DB-backed search corpus (search_documents + embeddings).
/// Implementations aggregate documents into one candidate cluster per video and emit raw
/// text/vector scores so <see cref="HybridRankingService"/> can perform the final blend.
/// </summary>
public interface ISearchCorpusSearcher
{
    /// <summary>
    /// Reports whether the corpus has any searchable, non-stale documents with succeeded
    /// embeddings. When false, the UI must render a waiting state rather than fabricated
    /// results (PRD §2.10).
    /// </summary>
    Task<SearchCorpusReadiness> GetReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the hybrid text+vector search and returns one candidate cluster per video.
    /// </summary>
    /// <param name="request">Normalized query, optional query embedding (null skips the vector leg), filters, settings, and result cap.</param>
    Task<IReadOnlyList<SearchCorpusCluster>> SearchAsync(SearchCorpusSearchRequest request, CancellationToken cancellationToken = default);
}

public sealed record SearchCorpusReadiness(bool HasSearchableCorpus, long SearchableDocumentCount);

public sealed record SearchCorpusSearchRequest(
    string Query,
    IReadOnlyList<double>? QueryEmbedding,
    string? QueryEmbeddingProvider,
    string? QueryEmbeddingModel,
    int? QueryEmbeddingDimensions,
    SearchFilters Filters,
    SearchUiSettings Settings,
    int MaxClusters = 25,
    int MaxDocumentsPerCluster = 8);

public sealed record SearchCorpusCluster(
    Guid VideoId,
    string ClusterId,
    string Title,
    string Channel,
    DateTimeOffset? PublishDate,
    string ResultType,
    bool HasTranscript,
    bool HasRepo,
    bool HasNotes,
    bool HasScreenshot,
    string ProcessingStatus,
    bool CanRetry,
    IReadOnlyList<SearchCorpusDocument> Documents,
    int RecentOpenCount,
    bool HasMatchingNote);

public sealed record SearchCorpusDocument(
    Guid SearchDocumentId,
    string DocumentType,
    double TextScore,
    double VectorScore,
    string? Snippet,
    IReadOnlyList<string> MatchedFields);
