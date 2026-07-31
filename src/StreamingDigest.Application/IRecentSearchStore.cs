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
}

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
