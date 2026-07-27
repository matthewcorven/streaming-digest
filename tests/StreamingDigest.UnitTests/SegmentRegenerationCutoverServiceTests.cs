using StreamingDigest.Application;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class SegmentRegenerationCutoverServiceTests
{
    [Fact]
    public void CreatePendingGeneration_RequiresApprovalAndEmbeddingReprocessing()
    {
        var service = new SegmentRegenerationCutoverService();
        var videoId = Guid.NewGuid();

        var generation = service.CreatePendingGeneration(
            videoId,
            "semantic_llm",
            2,
            [new Segment { VideoId = videoId, SourceType = "semantic_llm", Sequence = 1, TitleOriginal = "Intro" }]);

        Assert.True(generation.RequiresUserApproval);
        Assert.Equal("pending_approval", generation.Status);
        Assert.Single(generation.Segments);
        Assert.True(generation.Segments[0].RequiresEmbeddingApproval);
    }

    [Fact]
    public void ApproveCutover_ActivatesNewGeneration_PurgesOldScreenshots_AndSurfacesOrphanedNotes()
    {
        var service = new SegmentRegenerationCutoverService();
        var videoId = Guid.NewGuid();

        var oldGeneration = new SegmentGeneration
        {
            VideoId = videoId,
            SourceType = "author_chapter",
            GenerationVersion = 1,
            IsActive = true,
            RequiresUserApproval = false,
            Status = "active"
        };

        var oldSegment = new Segment
        {
            VideoId = videoId,
            SegmentGenerationId = oldGeneration.Id,
            SourceType = "author_chapter",
            Sequence = 1,
            TitleOriginal = "Existing segment"
        };

        oldGeneration.Segments.Add(oldSegment);
        oldGeneration.Screenshots.Add(new SegmentScreenshot
        {
            VideoId = videoId,
            SegmentGenerationId = oldGeneration.Id,
            IsActive = true
        });
        oldGeneration.Notes.Add(new SegmentNote
        {
            SegmentId = oldSegment.Id,
            Text = "Needs re-anchoring"
        });

        var pendingGeneration = service.CreatePendingGeneration(
            videoId,
            "semantic_llm",
            2,
            [new Segment { VideoId = videoId, SourceType = "semantic_llm", Sequence = 1, TitleOriginal = "New segment" }]);

        var result = service.ApproveCutover(pendingGeneration, [oldGeneration, pendingGeneration]);

        Assert.True(pendingGeneration.IsActive);
        Assert.False(oldGeneration.IsActive);
        Assert.Equal("active", pendingGeneration.Status);
        Assert.Empty(oldGeneration.Screenshots);
        Assert.Single(result.OrphanedNotes);
        Assert.Equal(DomainEventTypeCatalog.OrphanedNoteSurfaced, result.PendingInboxItems.Single().EventType);
        Assert.Contains(result.PendingInboxItems, item => item.Type == "orphaned_note");
        Assert.Contains(pendingGeneration.PendingInboxItems, item => item.Type == "orphaned_note");
    }

    [Fact]
    public void ApproveCutover_RequiresAnExplicitPendingApproval()
    {
        var service = new SegmentRegenerationCutoverService();
        var videoId = Guid.NewGuid();

        var alreadyApprovedGeneration = new SegmentGeneration
        {
            VideoId = videoId,
            SourceType = "semantic_llm",
            GenerationVersion = 2,
            RequiresUserApproval = false,
            Status = "active"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => service.ApproveCutover(alreadyApprovedGeneration, []));

        Assert.Equal("Only pending segment generations can be approved.", exception.Message);
    }
}
