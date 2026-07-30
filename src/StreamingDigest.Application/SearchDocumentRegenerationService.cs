using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

public interface ISearchDocumentRegenerationService
{
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForVideoAsync(Guid videoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForNoteAsync(Guid noteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForSegmentAsync(Guid segmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForTranscriptCueAsync(Guid cueId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForExternalResourceAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateAllAsync(CancellationToken cancellationToken = default);
}

public sealed class SearchDocumentRegenerationService : ISearchDocumentRegenerationService
{
    private readonly IStreamingDigestDbContext _context;
    private readonly ISearchDocumentGenerator _searchDocumentGenerator;
    private readonly ISearchDocumentEmbeddingStore _searchDocumentEmbeddingStore;

    public SearchDocumentRegenerationService(
        IStreamingDigestDbContext context,
        ISearchDocumentGenerator searchDocumentGenerator,
        ISearchDocumentEmbeddingStore searchDocumentEmbeddingStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _searchDocumentGenerator = searchDocumentGenerator ?? throw new ArgumentNullException(nameof(searchDocumentGenerator));
        _searchDocumentEmbeddingStore = searchDocumentEmbeddingStore ?? throw new ArgumentNullException(nameof(searchDocumentEmbeddingStore));
    }

    public Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        => entityType.Trim().ToLowerInvariant() switch
        {
            "video" => RegenerateForVideoAsync(entityId, cancellationToken),
            "segment" => RegenerateForSegmentAsync(entityId, cancellationToken),
            "transcript_cue" => RegenerateForTranscriptCueAsync(entityId, cancellationToken),
            "external_resource" => RegenerateForExternalResourceAsync(entityId, cancellationToken),
            "repository" => RegenerateForRepositoryAsync(entityId, cancellationToken),
            "note" => RegenerateForNoteAsync(entityId, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<StoredSearchDocumentEmbedding>>([])
        };

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForVideoAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        var video = await _context.Videos.SingleOrDefaultAsync(candidate => candidate.Id == videoId, cancellationToken);
        if (video is null)
        {
            return [];
        }

        await _searchDocumentEmbeddingStore.DeleteForVideoScopeAsync(video.Id, cancellationToken);

        var activeSegments = await _context.Segments
            .AsNoTracking()
            .Where(segment => segment.VideoId == videoId && segment.IsActive)
            .OrderBy(segment => segment.Sequence)
            .ToListAsync(cancellationToken);

        var activeSegmentIds = activeSegments.Select(segment => segment.Id).ToArray();
        var notes = await _context.Notes
            .AsNoTracking()
            .Where(note => note.DeletedAt == null
                && ((note.TargetType == "video" && note.TargetId == video.Id)
                    || (note.TargetType == "segment" && activeSegmentIds.Contains(note.TargetId))))
            .ToListAsync(cancellationToken);

        var activeTranscript = await _context.VideoTranscripts
            .AsNoTracking()
            .Include(transcript => transcript.Cues)
            .Where(transcript => transcript.VideoId == videoId && transcript.IsActive)
            .OrderByDescending(transcript => transcript.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = video.Id,
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(
                    video.Id,
                    video.Title,
                    video.TitleOverride,
                    video.DescriptionOriginal,
                    video.DescriptionOverride)
            ],
            SegmentTitlesAndSummaries = activeSegments
                .Select(segment => new SegmentTitleSummaryDocumentInput(
                    segment.Id,
                    segment.TitleOriginal,
                    segment.TitleOverride,
                    segment.SummaryOriginal,
                    segment.SummaryOverride))
                .ToArray(),
            TranscriptChunks = (activeTranscript?.Cues ?? [])
                .OrderBy(cue => cue.Sequence)
                .Select(cue => new TranscriptChunkDocumentInput(
                    cue.Id,
                    cue.TextOriginal,
                    cue.TextOverride,
                    cue.Sequence))
                .ToArray(),
            Notes = notes
                .Select(note => new NoteDocumentInput(
                    note.Id,
                    note.Markdown))
                .ToArray()
        };

        var documents = _searchDocumentGenerator.Generate(request);
        var stored = documents.Count == 0
            ? []
            : await _searchDocumentEmbeddingStore.StoreAsync(documents, cancellationToken: cancellationToken);

        video.SearchIndexedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await _context.Notes.SingleOrDefaultAsync(candidate => candidate.Id == noteId && candidate.DeletedAt == null, cancellationToken);
        if (note is null)
        {
            return [];
        }

        var parentVideoId = await ResolveParentVideoIdAsync(note, cancellationToken);
        await _searchDocumentEmbeddingStore.DeleteForSourceAsync(SearchDocumentSourceEntityTypes.Note, note.Id, cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            Notes = [new NoteDocumentInput(note.Id, note.Markdown)]
        };

        var documents = _searchDocumentGenerator.Generate(request);
        try
        {
            var stored = documents.Count == 0
                ? []
                : await _searchDocumentEmbeddingStore.StoreAsync(documents, cancellationToken: cancellationToken);

            note.EmbeddingStatus = "succeeded";
            await _context.SaveChangesAsync(cancellationToken);
            return stored;
        }
        catch
        {
            note.EmbeddingStatus = "failed";
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForSegmentAsync(Guid segmentId, CancellationToken cancellationToken = default)
    {
        var segment = await _context.Segments.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == segmentId, cancellationToken);
        if (segment is null)
        {
            return [];
        }

        await _searchDocumentEmbeddingStore.DeleteForSourceAsync(SearchDocumentSourceEntityTypes.Segment, segment.Id, cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = segment.VideoId,
            SegmentTitlesAndSummaries =
            [
                new SegmentTitleSummaryDocumentInput(
                    segment.Id,
                    segment.TitleOriginal,
                    segment.TitleOverride,
                    segment.SummaryOriginal,
                    segment.SummaryOverride)
            ]
        };

        return await RegenerateFromRequestAsync(request, segment.VideoId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForTranscriptCueAsync(Guid cueId, CancellationToken cancellationToken = default)
    {
        var cue = await _context.TranscriptCues.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == cueId, cancellationToken);
        if (cue is null)
        {
            return [];
        }

        var parentVideoId = await ResolveParentVideoIdAsync(cue, cancellationToken);
        await _searchDocumentEmbeddingStore.DeleteForSourceAsync(SearchDocumentSourceEntityTypes.TranscriptCue, cue.Id, cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            TranscriptChunks =
            [
                new TranscriptChunkDocumentInput(
                    cue.Id,
                    cue.TextOriginal,
                    cue.TextOverride,
                    cue.Sequence)
            ]
        };

        return await RegenerateFromRequestAsync(request, parentVideoId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForExternalResourceAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        var resource = await _context.ExternalResources.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == resourceId, cancellationToken);
        if (resource is null)
        {
            return [];
        }

        await _searchDocumentEmbeddingStore.DeleteForSourceAsync(SearchDocumentSourceEntityTypes.ExternalResource, resource.Id, cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = null,
            ExternalLinkMetadata =
            [
                new ExternalLinkMetadataDocumentInput(
                    resource.Id,
                    resource.TitleOriginal,
                    resource.TitleOverride,
                    resource.DescriptionOriginal,
                    resource.DescriptionOverride,
                    resource.ClassificationOriginal,
                    resource.ClassificationOverride,
                    resource.Domain,
                    resource.Domain)
            ]
        };

        return await RegenerateFromRequestAsync(request, null, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var repository = await _context.Repositories.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == repositoryId, cancellationToken);
        if (repository is null)
        {
            return [];
        }

        await _searchDocumentEmbeddingStore.DeleteForSourceAsync(SearchDocumentSourceEntityTypes.Repository, repository.Id, cancellationToken);

        var request = new SearchDocumentGenerationRequest
        {
            ParentVideoId = null,
            RepositoryReadmeChunks =
            [
                new RepositoryReadmeChunkDocumentInput(
                    repository.Id,
                    repository.DescriptionOriginal,
                    repository.DescriptionOverride)
            ]
        };

        return await RegenerateFromRequestAsync(request, null, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateAllAsync(CancellationToken cancellationToken = default)
    {
        var allResults = new List<StoredSearchDocumentEmbedding>();
 
        var videos = await _context.Videos.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var video in videos)
        {
            allResults.AddRange(await RegenerateForVideoAsync(video.Id, cancellationToken));
        }
 
        var segments = await _context.Segments.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var segment in segments)
        {
            allResults.AddRange(await RegenerateForSegmentAsync(segment.Id, cancellationToken));
        }
 
        var transcriptCues = await _context.TranscriptCues.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var cue in transcriptCues)
        {
            allResults.AddRange(await RegenerateForTranscriptCueAsync(cue.Id, cancellationToken));
        }
 
        var externalResources = await _context.ExternalResources.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var resource in externalResources)
        {
            allResults.AddRange(await RegenerateForExternalResourceAsync(resource.Id, cancellationToken));
        }
 
        var repositories = await _context.Repositories.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var repository in repositories)
        {
            allResults.AddRange(await RegenerateForRepositoryAsync(repository.Id, cancellationToken));
        }
 
        var notes = await _context.Notes.AsNoTracking().Where(note => note.DeletedAt == null).ToListAsync(cancellationToken);
        foreach (var note in notes)
        {
            allResults.AddRange(await RegenerateForNoteAsync(note.Id, cancellationToken));
        }
 
        return allResults;
    }
 
    private async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> RegenerateFromRequestAsync(
        SearchDocumentGenerationRequest request,
        Guid? parentVideoId,
        CancellationToken cancellationToken)
    {
        var documents = _searchDocumentGenerator.Generate(request);
        if (documents.Count == 0)
        {
            return [];
        }

        var stored = await _searchDocumentEmbeddingStore.StoreAsync(documents, cancellationToken: cancellationToken);
        if (parentVideoId is not null)
        {
            var video = await _context.Videos.SingleOrDefaultAsync(candidate => candidate.Id == parentVideoId.Value, cancellationToken);
            if (video is not null)
            {
                video.SearchIndexedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return stored;
    }

    private async Task<Guid?> ResolveParentVideoIdAsync(Note note, CancellationToken cancellationToken)
    {
        if (note.TargetType.Equals("video", StringComparison.OrdinalIgnoreCase))
        {
            var videoExists = await _context.Videos.AsNoTracking().AnyAsync(candidate => candidate.Id == note.TargetId, cancellationToken);
            return videoExists ? note.TargetId : null;
        }

        if (note.TargetType.Equals("segment", StringComparison.OrdinalIgnoreCase))
        {
            var segment = await _context.Segments.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == note.TargetId, cancellationToken);
            return segment?.VideoId;
        }

        return null;
    }

    private async Task<Guid?> ResolveParentVideoIdAsync(TranscriptCue cue, CancellationToken cancellationToken)
    {
        var transcript = await _context.VideoTranscripts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == cue.TranscriptId, cancellationToken);
        return transcript?.VideoId;
    }

}
