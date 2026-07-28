namespace StreamingDigest.Application;

public interface ISearchDocumentEmbeddingStore
{
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> StoreAsync(
        IEnumerable<GeneratedSearchDocument> documents,
        Guid? generatedByOperationId = null,
        CancellationToken cancellationToken = default);
}

public sealed record StoredSearchDocumentEmbedding(
    Guid SearchDocumentId,
    Guid EmbeddingId,
    string Provider,
    string Model,
    int Dimensions,
    string ContentHash,
    string SourceTextHash);
