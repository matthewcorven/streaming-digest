using StreamingDigest.Domain;

namespace StreamingDigest.Application;

public sealed class SegmentRegenerationCutoverService
{
    public SegmentGeneration CreatePendingGeneration(
        Guid videoId,
        string sourceType,
        int generationVersion,
        IReadOnlyCollection<Segment> segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentNullException.ThrowIfNull(segments);

        var generation = new SegmentGeneration
        {
            VideoId = videoId,
            SourceType = sourceType,
            GenerationVersion = generationVersion,
            RequiresUserApproval = true,
            Status = "pending_approval"
        };

        foreach (var segment in segments)
        {
            generation.Segments.Add(new Segment
            {
                SegmentGenerationId = generation.Id,
                Sequence = segment.Sequence,
                Title = segment.Title,
                IsActive = segment.IsActive,
                RequiresEmbeddingApproval = true
            });
        }

        return generation;
    }

    public SegmentRegenerationCutoverResult ApproveCutover(
        SegmentGeneration pendingGeneration,
        IReadOnlyCollection<SegmentGeneration> existingGenerations)
    {
        ArgumentNullException.ThrowIfNull(pendingGeneration);
        ArgumentNullException.ThrowIfNull(existingGenerations);

        if (!pendingGeneration.RequiresUserApproval)
        {
            throw new InvalidOperationException("Only pending segment generations can be approved.");
        }

        var priorGenerations = existingGenerations
            .Where(generation => generation.VideoId == pendingGeneration.VideoId && generation.Id != pendingGeneration.Id)
            .ToList();

        foreach (var generation in priorGenerations)
        {
            generation.IsActive = false;

            var screenshotsToPurge = generation.Screenshots.Where(screenshot => screenshot.IsActive).ToList();
            foreach (var screenshot in screenshotsToPurge)
            {
                generation.Screenshots.Remove(screenshot);
            }
        }

        pendingGeneration.IsActive = true;
        pendingGeneration.RequiresUserApproval = false;
        pendingGeneration.Status = "active";
        pendingGeneration.ActivatedAt = DateTimeOffset.UtcNow;

        var orphanedNotes = new List<SegmentNote>();
        var pendingInboxItems = new List<PendingActionItem>();

        foreach (var generation in priorGenerations)
        {
            foreach (var note in generation.Notes)
            {
                orphanedNotes.Add(note);
                pendingInboxItems.Add(new PendingActionItem
                {
                    Type = "orphaned_note",
                    Summary = $"Orphaned note requires re-anchoring or deletion: {note.Text}",
                    EventType = DomainEventTypeCatalog.OrphanedNoteSurfaced
                });
            }
        }

        pendingGeneration.PendingInboxItems.AddRange(pendingInboxItems);

        return new SegmentRegenerationCutoverResult(
            pendingGeneration,
            pendingInboxItems,
            orphanedNotes);
    }
}

public sealed record SegmentRegenerationCutoverResult(
    SegmentGeneration ActivatedGeneration,
    IReadOnlyCollection<PendingActionItem> PendingInboxItems,
    IReadOnlyCollection<SegmentNote> OrphanedNotes);
