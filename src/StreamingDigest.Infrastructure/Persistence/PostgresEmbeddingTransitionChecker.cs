using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Repositories;

namespace StreamingDigest.Infrastructure.Persistence;

/// <summary>
/// Queries the <c>operations</c> table to determine whether an embedding-regeneration
/// transition is currently in progress (ADR-0011, plan §4 D2).
/// </summary>
public sealed class PostgresEmbeddingTransitionChecker(IOperationStore operationStore) : IEmbeddingTransitionChecker
{
    private const string EmbeddingRegenerationOperationType = "reprocess.embeddings";

    /// <inheritdoc />
    public async Task<bool> IsTransitionActiveAsync(CancellationToken cancellationToken = default)
    {
        var active = await operationStore.GetActiveByTypeAsync(EmbeddingRegenerationOperationType, cancellationToken);
        return active.Count > 0;
    }

    /// <inheritdoc />
    public async Task<EmbeddingTransitionSnapshot?> GetLastCompletedTransitionAsync(CancellationToken cancellationToken = default)
    {
        var record = await operationStore.GetLastCompletedByTypeAsync(EmbeddingRegenerationOperationType, cancellationToken);
        if (record?.CompletedAt is null)
        {
            return null;
        }

        return new EmbeddingTransitionSnapshot(record.CompletedAt.Value);
    }
}
