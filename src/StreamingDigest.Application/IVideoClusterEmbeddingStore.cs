namespace StreamingDigest.Application;

public interface IVideoClusterEmbeddingStore
{
    Task<IReadOnlyList<StoredVideoClusterEmbedding>> BuildForVideoAsync(
        Guid videoId,
        Guid? generatedByOperationId = null,
        CancellationToken cancellationToken = default);

    Task MarkStaleForVideoAsync(
        Guid videoId,
        Guid? markedByOperationId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoClusterHighSignalMatch>> GetHighSignalMatchesAsync(
        Guid videoId,
        double thresholdPercent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoClusterRelatedItem>> GetRelatedVideosAsync(
        Guid videoId,
        int take = 3,
        CancellationToken cancellationToken = default);
}

public sealed record StoredVideoClusterEmbedding(
    Guid Id,
    Guid VideoId,
    string Provider,
    string Model,
    int Dimensions,
    string ContentHash,
    bool IsStale,
    string ComponentWeightsJson,
    Guid? GeneratedByOperationId,
    Guid? StaleMarkedByOperationId);

public sealed record VideoClusterHighSignalMatch(
    Guid RecentSearchId,
    string QueryText,
    double SimilarityPercent,
    string Provider,
    string Model,
    int Dimensions);

public sealed record VideoClusterRelatedItem(
    Guid VideoId,
    double SimilarityPercent,
    double RelativeSimilarityPercent,
    string Provider,
    string Model,
    int Dimensions);
