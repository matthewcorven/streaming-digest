using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class IngestionItemRepository(StreamingDigestDbContext context) : IIngestionItemRepository
{
    public async Task<IngestionItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.IngestionItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<List<IngestionItem>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default)
        => await context.IngestionItems
            .Where(item => item.IngestionRunId == runId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<List<IngestionItem>> GetByRunIdWithStageStatusAsync(Guid runId, string stageName, string status, CancellationToken cancellationToken = default)
    {
        return stageName.ToLower() switch
        {
            "transcript" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.TranscriptStatus == status)
                .ToListAsync(cancellationToken),
            "segments" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.SegmentsStatus == status)
                .ToListAsync(cancellationToken),
            "screenshots" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ScreenshotsStatus == status)
                .ToListAsync(cancellationToken),
            "links" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.LinksStatus == status)
                .ToListAsync(cancellationToken),
            "repos" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ReposStatus == status)
                .ToListAsync(cancellationToken),
            "websites" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.WebsitesStatus == status)
                .ToListAsync(cancellationToken),
            "embeddings" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.EmbeddingsStatus == status)
                .ToListAsync(cancellationToken),
            _ => []
        };
    }

    public async Task AddAsync(IngestionItem item, CancellationToken cancellationToken = default)
    {
        await context.IngestionItems.AddAsync(item, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddBulkAsync(IEnumerable<IngestionItem> items, CancellationToken cancellationToken = default)
    {
        await context.IngestionItems.AddRangeAsync(items, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(IngestionItem item, CancellationToken cancellationToken = default)
    {
        item.UpdatedAt = DateTimeOffset.UtcNow;
        context.IngestionItems.Update(item);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid itemId, string status, CancellationToken cancellationToken = default)
    {
        var item = await GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Status = status;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpdateAsync(item, cancellationToken);
    }

    public async Task UpdateStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken = default)
    {
        var item = await GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        switch (stageName.ToLower())
        {
            case "transcript":
                item.TranscriptStatus = status;
                break;
            case "segments":
                item.SegmentsStatus = status;
                break;
            case "screenshots":
                item.ScreenshotsStatus = status;
                break;
            case "links":
                item.LinksStatus = status;
                break;
            case "repos":
                item.ReposStatus = status;
                break;
            case "websites":
                item.WebsitesStatus = status;
                break;
            case "embeddings":
                item.EmbeddingsStatus = status;
                break;
        }

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpdateAsync(item, cancellationToken);
    }

    public async Task BulkUpdateStageStatusAsync(Guid runId, string stageName, string fromStatus, string toStatus, CancellationToken cancellationToken = default)
    {
        var items = await GetByRunIdWithStageStatusAsync(runId, stageName, fromStatus, cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            await UpdateStageStatusAsync(item.Id, stageName, toStatus, cancellationToken);
        }
    }
}
