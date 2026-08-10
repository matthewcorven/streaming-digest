using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Repositories;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// EF Core implementation of the per-video pipeline persistence surface. Bound to one DI
/// scope's <see cref="StreamingDigestDbContext"/>; registered scoped so each video pipeline
/// gets its own instance (and its own change tracker).
/// </summary>
public sealed class EfVideoPipelinePersistence(
    StreamingDigestDbContext context,
    IIngestionItemRepository items) : IVideoPipelinePersistence
{
    /// <inheritdoc />
    public async Task PersistPipelineChangesAsync(VideoPipelineContext pipeline, CancellationToken cancellationToken)
    {
        if (pipeline.SegmentGeneration is not null && context.Entry(pipeline.SegmentGeneration).State == EntityState.Detached)
        {
            await context.SegmentGenerations.AddAsync(pipeline.SegmentGeneration, cancellationToken);

            // SegmentGeneration.Segments is ignored by the EF configuration (read-only nav),
            // so cascade-insert won't flow; persist the child rows explicitly.
            if (pipeline.SegmentGeneration.Segments.Count > 0)
            {
                await context.Segments.AddRangeAsync(pipeline.SegmentGeneration.Segments, cancellationToken);
            }
        }

        var newResources = pipeline.Resources.Where(r => context.Entry(r).State == EntityState.Detached).ToList();
        if (newResources.Count > 0)
        {
            await context.ExternalResources.AddRangeAsync(newResources, cancellationToken);
        }

        var newRepositories = pipeline.Repositories.Where(r => context.Entry(r).State == EntityState.Detached).ToList();
        if (newRepositories.Count > 0)
        {
            await context.Repositories.AddRangeAsync(newRepositories, cancellationToken);
        }

        var newPages = pipeline.ScrapedPages
            .Where(p => context.Entry(p).State == EntityState.Detached)
            .Where(p => !context.ScrapedPages.Local.Any(existing =>
                existing.ExternalResourceId == p.ExternalResourceId && existing.ScrapedAt == p.ScrapedAt))
            .ToList();
        if (newPages.Count > 0)
        {
            // ScraperClient persists its own ScrapedPage row per call when constructed with a
            // DbContext; skip pages that are already tracked for this resource to avoid doubles.
            var persistedResourceIds = await context.ScrapedPages
                .Where(sp => newPages.Select(p => p.ExternalResourceId).Contains(sp.ExternalResourceId))
                .Select(sp => sp.ExternalResourceId)
                .ToListAsync(cancellationToken);
            foreach (var page in newPages.Where(p => !persistedResourceIds.Contains(p.ExternalResourceId)))
            {
                await context.ScrapedPages.AddAsync(page, cancellationToken);
            }
        }

        var newEvents = pipeline.PendingEvents.Where(e => context.Entry(e).State == EntityState.Detached).ToList();
        if (newEvents.Count > 0)
        {
            await context.DomainEvents.AddRangeAsync(newEvents, cancellationToken);
            pipeline.PendingEvents.Clear();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task SetStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken)
        => items.UpdateStageStatusAsync(itemId, stageName, status, cancellationToken);

    /// <inheritdoc />
    public async Task FinalizeItemAsync(Guid itemId, string status, string? errorSummary, CancellationToken cancellationToken)
    {
        var item = await context.IngestionItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        item.Status = status;
        item.ErrorSummary = errorSummary;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetVideoIngestionStatusAsync(Guid videoId, string status, Guid? runId, bool succeeded, CancellationToken cancellationToken)
    {
        var video = await context.Videos.FirstOrDefaultAsync(v => v.Id == videoId, cancellationToken);
        if (video is null)
        {
            return;
        }

        video.IngestionStatus = status;
        if (runId is not null)
        {
            if (succeeded)
            {
                video.LastSuccessfulIngestionRunId = runId;
            }
            else
            {
                video.LastFailedIngestionRunId = runId;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
