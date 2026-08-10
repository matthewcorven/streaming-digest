using StreamingDigest.Domain;

namespace StreamingDigest.Application.Repositories;

public interface IIngestionRunRepository
{
    Task<IngestionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<IngestionRun>> GetListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task AddAsync(IngestionRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(IngestionRun run, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid runId, string status, CancellationToken cancellationToken = default);
}
