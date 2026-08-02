using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class IngestionRunRepository(StreamingDigestDbContext context) : IIngestionRunRepository
{
    public async Task<IngestionRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.IngestionRuns.FirstOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<List<IngestionRun>> GetListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
        => await context.IngestionRuns
            .OrderByDescending(run => run.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(IngestionRun run, CancellationToken cancellationToken = default)
    {
        await context.IngestionRuns.AddAsync(run, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(IngestionRun run, CancellationToken cancellationToken = default)
    {
        context.IngestionRuns.Update(run);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid runId, string status, CancellationToken cancellationToken = default)
    {
        var run = await GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        run.Status = status;
        await UpdateAsync(run, cancellationToken);
    }
}
