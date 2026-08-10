using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Repositories;
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
        var normalizedStatus = StageStatusConstants.Normalize(status);
        
        return stageName.ToLower() switch
        {
            "transcript" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.TranscriptStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "segments" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.SegmentsStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "screenshots" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ScreenshotsStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "links" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.LinksStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "repos" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ReposStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "websites" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.WebsitesStatus == normalizedStatus)
                .ToListAsync(cancellationToken),
            "embeddings" => await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.EmbeddingsStatus == normalizedStatus)
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
        var normalizedStatus = StageStatusConstants.Normalize(status);
        
        var item = await GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Status = normalizedStatus;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpdateAsync(item, cancellationToken);
    }

    public async Task UpdateStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken = default)
    {
        var normalizedStageName = stageName.ToLowerInvariant();
        var normalizedStatus = StageStatusConstants.Normalize(status);
        
        var item = await GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        switch (normalizedStageName)
        {
            case "transcript":
                item.TranscriptStatus = normalizedStatus;
                break;
            case "segments":
                item.SegmentsStatus = normalizedStatus;
                break;
            case "screenshots":
                item.ScreenshotsStatus = normalizedStatus;
                break;
            case "links":
                item.LinksStatus = normalizedStatus;
                break;
            case "repos":
                item.ReposStatus = normalizedStatus;
                break;
            case "websites":
                item.WebsitesStatus = normalizedStatus;
                break;
            case "embeddings":
                item.EmbeddingsStatus = normalizedStatus;
                break;
            default:
                throw new ArgumentException($"Unknown stage name: '{stageName}'", nameof(stageName));
        }

        item.UpdatedAt = DateTimeOffset.UtcNow;
        await UpdateAsync(item, cancellationToken);
    }

    public async Task BulkUpdateStageStatusAsync(Guid runId, string stageName, string fromStatus, string toStatus, CancellationToken cancellationToken = default)
    {
        var normalizedStageName = stageName.ToLowerInvariant();
        var normalizedFromStatus = StageStatusConstants.Normalize(fromStatus);
        var normalizedToStatus = StageStatusConstants.Normalize(toStatus);

        if (normalizedStageName == "transcript")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.TranscriptStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.TranscriptStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "segments")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.SegmentsStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.SegmentsStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "screenshots")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ScreenshotsStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ScreenshotsStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "links")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.LinksStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LinksStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "repos")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.ReposStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ReposStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "websites")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.WebsitesStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.WebsitesStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else if (normalizedStageName == "embeddings")
        {
            await context.IngestionItems
                .Where(item => item.IngestionRunId == runId && item.EmbeddingsStatus == normalizedFromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.EmbeddingsStatus, normalizedToStatus)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        else
        {
            throw new ArgumentException($"Unknown stage name: '{stageName}'", nameof(stageName));
        }
        
        // Clear change tracker to ensure fresh fetch on next query
        context.ChangeTracker.Clear();
    }
}
