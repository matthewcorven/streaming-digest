using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public interface IIngestionItemRepository
{
    Task<IngestionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<IngestionItem>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<List<IngestionItem>> GetByRunIdWithStageStatusAsync(Guid runId, string stageName, string status, CancellationToken cancellationToken = default);
    Task AddAsync(IngestionItem item, CancellationToken cancellationToken = default);
    Task AddBulkAsync(IEnumerable<IngestionItem> items, CancellationToken cancellationToken = default);
    Task UpdateAsync(IngestionItem item, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid itemId, string status, CancellationToken cancellationToken = default);
    Task UpdateStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken = default);
    Task BulkUpdateStageStatusAsync(Guid runId, string stageName, string fromStatus, string toStatus, CancellationToken cancellationToken = default);
}
