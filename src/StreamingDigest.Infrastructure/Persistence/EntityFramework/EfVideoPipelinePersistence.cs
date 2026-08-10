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
            // Segment generations are unique on (video_id, generation_version). On reprocess the
            // pipeline re-derives a generation (version 1) that already exists, so assign the next
            // free version for this video instead of colliding on the unique constraint (change 4).
            var maxExistingVersion = await context.SegmentGenerations
                .Where(g => g.VideoId == pipeline.SegmentGeneration.VideoId)
                .Select(g => (int?)g.GenerationVersion)
                .MaxAsync(cancellationToken);
            var nextVersion = (maxExistingVersion ?? 0) + 1;
            if (pipeline.SegmentGeneration.GenerationVersion != nextVersion)
            {
                pipeline.SegmentGeneration.GenerationVersion = nextVersion;
            }

            await context.SegmentGenerations.AddAsync(pipeline.SegmentGeneration, cancellationToken);

            // SegmentGeneration.Segments is ignored by the EF configuration (read-only nav),
            // so cascade-insert won't flow; persist the child rows explicitly and save BEFORE
            // adding screenshots. Screenshots carry a FK to segments, so the segment rows must
            // exist first — adding both in one SaveChanges would let EF order the screenshot
            // insert before its segment and violate the screenshots_segment_id_fkey.
            if (pipeline.SegmentGeneration.Segments.Count > 0)
            {
                await context.Segments.AddRangeAsync(pipeline.SegmentGeneration.Segments, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            // Now that the parent segment rows exist, persist the screenshots explicitly (the
            // nav is EF-ignored, so without this the rows would silently vanish).
            if (pipeline.SegmentGeneration.Screenshots.Count > 0)
            {
                await context.SegmentScreenshots.AddRangeAsync(pipeline.SegmentGeneration.Screenshots, cancellationToken);
            }
        }

        // External resources and repositories are canonical rows keyed by canonical_url
        // (unique constraint). Reprocessing a video re-derives the same rows, so upsert:
        // reuse the persisted row when one exists, insert otherwise. This keeps reprocess
        // idempotent instead of throwing on the unique constraint.
        await UpsertResourcesAsync(pipeline, cancellationToken);
        await UpsertRepositoriesAsync(pipeline, cancellationToken);

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

    private async Task UpsertResourcesAsync(VideoPipelineContext pipeline, CancellationToken cancellationToken)
    {
        var candidates = pipeline.Resources
            .Where(r => context.Entry(r).State == EntityState.Detached)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var canonicalUrls = candidates.Select(r => r.CanonicalUrl).Distinct(StringComparer.Ordinal).ToList();
        var existing = await context.ExternalResources
            .Where(r => canonicalUrls.Contains(r.CanonicalUrl))
            .ToDictionaryAsync(r => r.CanonicalUrl, StringComparer.Ordinal, cancellationToken);

        foreach (var resource in candidates)
        {
            if (existing.TryGetValue(resource.CanonicalUrl, out var persisted))
            {
                // Reuse the canonical row: refresh metadata the reprocess re-derived and
                // point downstream consumers (websites/embeddings) at the persisted id.
                persisted.ResourceType = resource.ResourceType;
                persisted.Domain ??= resource.Domain;
                persisted.TitleOriginal ??= resource.TitleOriginal;
                persisted.DescriptionOriginal ??= resource.DescriptionOriginal;
                persisted.ClassificationOriginal = resource.ClassificationOriginal;
                persisted.ClassificationConfidence ??= resource.ClassificationConfidence;
                persisted.ClassificationMethod ??= resource.ClassificationMethod;
                persisted.IsAdOrSponsor = resource.IsAdOrSponsor;
                pipeline.Resources[pipeline.Resources.IndexOf(resource)] = persisted;
            }
            else
            {
                await context.ExternalResources.AddAsync(resource, cancellationToken);
            }
        }
    }

    private async Task UpsertRepositoriesAsync(VideoPipelineContext pipeline, CancellationToken cancellationToken)
    {
        var candidates = pipeline.Repositories
            .Where(r => context.Entry(r).State == EntityState.Detached)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var canonicalUrls = candidates.Select(r => r.CanonicalUrl).Distinct(StringComparer.Ordinal).ToList();
        var existing = await context.Repositories
            .Where(r => canonicalUrls.Contains(r.CanonicalUrl))
            .ToDictionaryAsync(r => r.CanonicalUrl, StringComparer.Ordinal, cancellationToken);

        foreach (var repository in candidates)
        {
            if (existing.TryGetValue(repository.CanonicalUrl, out var persisted))
            {
                // Refresh metadata the reprocess re-fetched (stars/descriptions drift over time).
                persisted.Host = repository.Host;
                persisted.Owner ??= repository.Owner;
                persisted.Name ??= repository.Name;
                persisted.NormalizedOwner ??= repository.NormalizedOwner;
                persisted.NormalizedName ??= repository.NormalizedName;
                persisted.DefaultBranch ??= repository.DefaultBranch;
                persisted.DescriptionOriginal ??= repository.DescriptionOriginal;
                persisted.Stars = repository.Stars;
                persisted.PrimaryLanguage ??= repository.PrimaryLanguage;
                persisted.LicenseSpdxId ??= repository.LicenseSpdxId;
                persisted.DeepwikiUrl ??= repository.DeepwikiUrl;
            }
            else
            {
                await context.Repositories.AddAsync(repository, cancellationToken);
            }
        }
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
    public async Task SetVideoIngestionStatusAsync(
        Guid videoId,
        string status,
        Guid? runId,
        bool succeeded,
        ScreenshotStageOutcome screenshotOutcome,
        CancellationToken cancellationToken)
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

        // Maintain screenshot_status alongside ingestion status so API/UI consumers stop
        // reading "unknown" forever (mirrors how TranscriptIngestionService maintains
        // transcript_status).
        video.ScreenshotStatus = screenshotOutcome switch
        {
            ScreenshotStageOutcome.Generated => "completed",
            ScreenshotStageOutcome.Deferred => "deferred",
            ScreenshotStageOutcome.PartialFailure => "failed",
            _ => video.ScreenshotStatus,
        };

        await context.SaveChangesAsync(cancellationToken);
    }
}
