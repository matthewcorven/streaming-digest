using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Api.Endpoints;

internal static class OverridesEndpoints
{
    public static void MapOverrideEndpoints(this WebApplication app)
    {
        app.MapGet("/api/videos/{videoId:guid}/overrides", async (Guid videoId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var video = await context.Videos
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);

            if (video is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                titleOriginal = video.Title,
                titleOverride = video.TitleOverride,
                authorOriginal = video.AuthorOriginal,
                authorOverride = video.AuthorOverride,
                descriptionOriginal = video.DescriptionOriginal,
                descriptionOverride = video.DescriptionOverride
            });
        });

        app.MapGet("/api/external-resources/{resourceId:guid}/overrides", async (Guid resourceId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var resource = await context.ExternalResources
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);

            if (resource is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                titleOriginal = resource.TitleOriginal,
                titleOverride = resource.TitleOverride,
                descriptionOriginal = resource.DescriptionOriginal,
                descriptionOverride = resource.DescriptionOverride,
                classificationOriginal = resource.ClassificationOriginal,
                classificationOverride = resource.ClassificationOverride
            });
        });

        app.MapPut("/api/videos/{videoId:guid}/overrides", async (Guid videoId, UpdateVideoOverrideRequest request, StreamingDigestDbContext context, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var video = await context.Videos.SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
            if (video is null)
            {
                return Results.NotFound();
            }

            var historyEntries = new List<FieldOverrideHistory>();
            if (request.Title is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "title", PreviousValue = video.TitleOverride, NewValue = newValue });
                video.TitleOverride = newValue;
            }
            if (request.Author is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "author", PreviousValue = video.AuthorOverride, NewValue = newValue });
                video.AuthorOverride = newValue;
            }
            if (request.Description is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "description", PreviousValue = video.DescriptionOverride, NewValue = newValue });
                video.DescriptionOverride = newValue;
            }

            context.FieldOverrideHistories.AddRange(historyEntries);
            await context.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("video", videoId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPut("/api/segments/{segmentId:guid}/overrides", async (Guid segmentId, UpdateSegmentOverrideRequest request, StreamingDigestDbContext context, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var segment = await context.Segments.SingleOrDefaultAsync(s => s.Id == segmentId, cancellationToken);
            if (segment is null)
            {
                return Results.NotFound();
            }

            var historyEntries = new List<FieldOverrideHistory>();
            if (request.Title is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "segment", EntityId = segmentId, FieldName = "title", PreviousValue = segment.TitleOverride, NewValue = newValue });
                segment.TitleOverride = newValue;
            }
            if (request.Summary is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "segment", EntityId = segmentId, FieldName = "summary", PreviousValue = segment.SummaryOverride, NewValue = newValue });
                segment.SummaryOverride = newValue;
            }

            context.FieldOverrideHistories.AddRange(historyEntries);
            await context.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("segment", segmentId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPut("/api/transcript-cues/{cueId:guid}/overrides", async (Guid cueId, UpdateTranscriptCueOverrideRequest request, StreamingDigestDbContext context, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var cue = await context.TranscriptCues.SingleOrDefaultAsync(candidate => candidate.Id == cueId, cancellationToken);
            if (cue is null)
            {
                return Results.NotFound();
            }

            var newValue = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();
            context.FieldOverrideHistories.Add(new FieldOverrideHistory { EntityType = "transcript_cue", EntityId = cueId, FieldName = "text", PreviousValue = cue.TextOverride, NewValue = newValue });
            cue.TextOverride = newValue;
            await context.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("transcript_cue", cueId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPut("/api/external-resources/{resourceId:guid}/overrides", async (Guid resourceId, UpdateExternalResourceOverrideRequest request, StreamingDigestDbContext context, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var resource = await context.ExternalResources.SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
            if (resource is null)
            {
                return Results.NotFound();
            }

            var historyEntries = new List<FieldOverrideHistory>();
            if (request.Title is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "title", PreviousValue = resource.TitleOverride, NewValue = newValue });
                resource.TitleOverride = newValue;
            }
            if (request.Description is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "description", PreviousValue = resource.DescriptionOverride, NewValue = newValue });
                resource.DescriptionOverride = newValue;
            }
            if (request.Classification is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Classification) ? null : request.Classification.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "classification", PreviousValue = resource.ClassificationOverride, NewValue = newValue });
                resource.ClassificationOverride = newValue;
            }

            context.FieldOverrideHistories.AddRange(historyEntries);
            await context.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("external_resource", resourceId, cancellationToken);
            return Results.NoContent();
        });

        app.MapPut("/api/repositories/{repositoryId:guid}/overrides", async (Guid repositoryId, UpdateRepositoryOverrideRequest request, StreamingDigestDbContext context, ISearchDocumentRegenerationService regenerationService, CancellationToken cancellationToken) =>
        {
            var repo = await context.Repositories.SingleOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);
            if (repo is null)
            {
                return Results.NotFound();
            }

            var historyEntries = new List<FieldOverrideHistory>();
            if (request.Description is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "description", PreviousValue = repo.DescriptionOverride, NewValue = newValue });
                repo.DescriptionOverride = newValue;
            }
            if (request.PrimaryLanguage is not null)
            {
                var newValue = string.IsNullOrWhiteSpace(request.PrimaryLanguage) ? null : request.PrimaryLanguage.Trim();
                historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "primary_language", PreviousValue = repo.PrimaryLanguage, NewValue = newValue });
                repo.PrimaryLanguage = newValue;
            }
            if (request.Topics is not null)
            {
                var newTopics = request.Topics.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
                var previousTopics = repo.Topics is { Length: > 0 } ? string.Join(",", repo.Topics) : null;
                var newTopicsStr = newTopics.Length > 0 ? string.Join(",", newTopics) : null;
                historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "topics", PreviousValue = previousTopics, NewValue = newTopicsStr });
                repo.Topics = newTopics.Length > 0 ? newTopics : null;
            }

            context.FieldOverrideHistories.AddRange(historyEntries);
            await context.SaveChangesAsync(cancellationToken);
            await regenerationService.RegenerateForEntityAsync("repository", repositoryId, cancellationToken);
            return Results.NoContent();
        });

        app.MapGet("/api/overrides/history", async (string? entityType, Guid? entityId, string? fieldName, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var query = context.FieldOverrideHistories.AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(h => h.EntityType == entityType);
            }
            if (entityId.HasValue)
            {
                query = query.Where(h => h.EntityId == entityId.Value);
            }
            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                query = query.Where(h => h.FieldName == fieldName);
            }

            var entries = await query
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new FieldOverrideHistoryResponse(h.Id, h.EntityType, h.EntityId, h.FieldName, h.PreviousValue, h.NewValue, h.ChangedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(entries);
        });
    }
}

internal sealed record UpdateTranscriptCueOverrideRequest(string? Text);
internal sealed record UpdateVideoOverrideRequest(string? Title, string? Author, string? Description);
internal sealed record UpdateSegmentOverrideRequest(string? Title, string? Summary);
internal sealed record UpdateExternalResourceOverrideRequest(string? Title, string? Description, string? Classification);
internal sealed record UpdateRepositoryOverrideRequest(string? Description, string? PrimaryLanguage, string[]? Topics);
internal sealed record FieldOverrideHistoryResponse(Guid Id, string EntityType, Guid EntityId, string FieldName, string? PreviousValue, string? NewValue, DateTimeOffset ChangedAt);