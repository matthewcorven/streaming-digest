namespace StreamingDigest.Application;

public interface IRecentSearchStore
{
    Task<StoredRecentSearch> StoreSearchAsync(
        string query,
        SearchFilters filters,
        SearchUiSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListRecentQueriesAsync(
        int take = 8,
        CancellationToken cancellationToken = default);

    Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default);

    Task RecordInteractionAsync(
        SearchInteractionEvent interaction,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetRecentOpenCountsAsync(
        IEnumerable<Guid> videoIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recently stored query embedding for a recent search, or null when
    /// the embedding was never persisted (e.g. the embedding service was unavailable). The
    /// hybrid vector leg is skipped when this returns null.
    /// </summary>
    Task<StoredQueryEmbedding?> GetQueryEmbeddingAsync(
        Guid recentSearchId,
        CancellationToken cancellationToken = default);
}

public sealed record StoredQueryEmbedding(
    string Provider,
    string Model,
    int Dimensions,
    IReadOnlyList<double> Values);

public sealed record StoredRecentSearch(
    Guid Id,
    string QueryText,
    DateTimeOffset SearchedAt);

public sealed record SearchInteractionEvent(
    Guid? RecentSearchId,
    Guid VideoId,
    Guid? SearchDocumentId,
    string ResultType,
    string EventType,
    string? MetadataJson,
    DateTimeOffset ActivatedAt);
