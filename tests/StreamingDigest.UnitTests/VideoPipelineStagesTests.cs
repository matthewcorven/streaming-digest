using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamingDigest.Application;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

using IModelReadinessGuard = StreamingDigest.Application.Orchestration.IModelReadinessGuard;

namespace StreamingDigest.UnitTests;

public sealed class VideoPipelineStagesTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    private static VideoPipelineContext CreateContext(Video? video = null)
    {
        var v = video ?? new Video(Guid.NewGuid(), "Test Video") { YoutubeVideoId = "abc12345678" };
        var run = new IngestionRun { Id = RunId, Status = "running" };
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = RunId,
            ItemType = "video",
            ItemId = v.Id,
            Stage = IngestionStageNames.Transcript,
            Status = "pending",
        };
        return new VideoPipelineContext { Video = v, Item = item, Run = run };
    }

    private sealed class StubReadinessGuard(bool ready) : IModelReadinessGuard
    {
        public Task<bool> IsReadyAsync(string capability, CancellationToken cancellationToken = default)
            => Task.FromResult(ready);
    }

    private sealed class FakeLinkExtractionService(IReadOnlyList<ExtractedVideoLink> links) : IVideoLinkExtractionService
    {
        public IReadOnlyList<ExtractedVideoLink> Extract(string? descriptionText, string? pinnedCommentText)
            => links;
    }

    // ── Links stage: heuristic fallback notification when LLM unready ────────────

    [Fact]
    public async Task LinksStage_emits_capability_unready_event_when_llm_not_ready()
    {
        var links = new List<ExtractedVideoLink> { new("https://github.com/example/repo", VideoLinkSource.Description) };
        var context = CreateContext();
        var handler = new LinksStageHandler(
            new FakeLinkExtractionService(links),
            new LinkClassificationService(),
            new StubReadinessGuard(ready: false),
            NullLogger<LinksStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Resources);
        Assert.Contains(context.Warnings, w => w.Contains("heuristic", StringComparison.OrdinalIgnoreCase));
        var evt = Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
        Assert.Equal("warning", evt.Severity);
        Assert.Equal(context.Video.Id, evt.EntityId);
        Assert.Equal(RunId, evt.IngestionRunId);
    }

    [Fact]
    public async Task LinksStage_no_fallback_event_when_llm_ready()
    {
        var links = new List<ExtractedVideoLink> { new("https://github.com/example/repo", VideoLinkSource.Description) };
        var context = CreateContext();
        var handler = new LinksStageHandler(
            new FakeLinkExtractionService(links),
            new LinkClassificationService(),
            new StubReadinessGuard(ready: true),
            NullLogger<LinksStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.Resources);
        Assert.Equal("repository", context.Resources[0].ResourceType);
        Assert.DoesNotContain(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
    }

    [Fact]
    public async Task LinksStage_emits_event_only_once_for_multiple_links()
    {
        var links = new List<ExtractedVideoLink>
        {
            new("https://github.com/example/one", VideoLinkSource.Description),
            new("https://github.com/example/two", VideoLinkSource.Description),
        };
        var context = CreateContext();
        var handler = new LinksStageHandler(
            new FakeLinkExtractionService(links),
            new LinkClassificationService(),
            new StubReadinessGuard(ready: false),
            NullLogger<LinksStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, context.Resources.Count);
        Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
    }

    [Fact]
    public async Task LinksStage_no_links_is_trivial_success()
    {
        var context = CreateContext();
        var handler = new LinksStageHandler(
            new FakeLinkExtractionService([]),
            new LinkClassificationService(),
            new StubReadinessGuard(ready: false),
            NullLogger<LinksStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(context.Resources);
        Assert.Empty(context.Warnings);
        Assert.Empty(context.PendingEvents);
    }

    // ── Segments stage: LLM-guarded deterministic fallback ───────────────────────

    private static VideoPipelineContext CreateTranscriptContext()
    {
        var context = CreateContext();
        var transcript = new VideoTranscript
        {
            VideoId = context.Video.Id,
            SourceType = VideoTranscriptSourceTypes.YouTubeCaption,
        };
        for (var i = 0; i < 5; i++)
        {
            transcript.Cues.Add(new TranscriptCue
            {
                TranscriptId = transcript.Id,
                Sequence = i,
                StartSeconds = i * 60m,
                EndSeconds = (i + 1) * 60m,
                TextOriginal = $"Transcript sentence {i}.",
            });
        }

        context.Transcript = transcript;
        return context;
    }

    [Fact]
    public async Task SegmentsStage_emits_capability_unready_when_llm_not_ready_and_no_chapters()
    {
        var context = CreateTranscriptContext();
        var handler = new SegmentsStageHandler(
            new AuthorChapterSegmentationService(),
            new DeterministicTranscriptChunkingService(),
            new StubReadinessGuard(ready: false),
            NullLogger<SegmentsStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.SegmentGeneration);
        Assert.Contains(context.Warnings, w => w.Contains("deterministic", StringComparison.OrdinalIgnoreCase));
        Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
    }

    [Fact]
    public async Task SegmentsStage_uses_author_chapters_without_llm_check()
    {
        var context = CreateTranscriptContext();
        context.Video.ChaptersJson = """[{"title":"Intro","startSeconds":0,"endSeconds":60}]""";
        var handler = new SegmentsStageHandler(
            new AuthorChapterSegmentationService(),
            new DeterministicTranscriptChunkingService(),
            new StubReadinessGuard(ready: false), // unready, but chapters win
            NullLogger<SegmentsStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.SegmentGeneration);
        Assert.DoesNotContain(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
    }

    [Fact]
    public async Task SegmentsStage_skips_when_no_transcript()
    {
        var context = CreateContext();
        var handler = new SegmentsStageHandler(
            new AuthorChapterSegmentationService(),
            new DeterministicTranscriptChunkingService(),
            new StubReadinessGuard(ready: true),
            NullLogger<SegmentsStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Null(context.SegmentGeneration);
        Assert.Empty(context.Warnings);
    }

    // ── Processor: stage failure → item failed + IngestionStageFailed event ─────

    private sealed class ThrowingStage : IVideoStageHandler
    {
        public string StageName => IngestionStageNames.Transcript;
        public Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private sealed class NoOpStage(string name) : IVideoStageHandler
    {
        public string StageName => name;
        public Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingPersistence : IVideoPipelinePersistence
    {
        public List<(string Stage, string Status)> StageStatuses { get; } = [];
        public int PersistCount { get; private set; }

        public Task PersistPipelineChangesAsync(VideoPipelineContext context, CancellationToken cancellationToken)
        {
            PersistCount++;
            return Task.CompletedTask;
        }

        public Task SetStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken)
        {
            StageStatuses.Add((stageName, status));
            return Task.CompletedTask;
        }

        public Task FinalizeItemAsync(Guid itemId, string status, string? errorSummary, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetVideoIngestionStatusAsync(
            Guid videoId,
            string status,
            Guid? runId,
            bool succeeded,
            ScreenshotStageOutcome screenshotOutcome,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task Processor_marks_stage_failed_and_stops_pipeline_on_unexpected_exception()
    {
        var persistence = new RecordingPersistence();
        var processor = new VideoPipelineProcessor(
            persistence,
            [new NoOpStage(IngestionStageNames.Transcript), new ThrowingStage(), new NoOpStage(IngestionStageNames.Segments)]);
        var context = CreateContext();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.True(context.StageFailed);
        Assert.Equal(
            [(IngestionStageNames.Transcript, IngestionStageStatuses.Completed), (IngestionStageNames.Transcript, IngestionStageStatuses.Failed)],
            persistence.StageStatuses);
        // The Segments stage never ran (no third SetStageStatusAsync call).
        Assert.DoesNotContain(persistence.StageStatuses, s => s.Stage == IngestionStageNames.Segments);
        var evt = Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.IngestionStageFailed);
        Assert.Equal("error", evt.Severity);
        Assert.Equal(1, persistence.PersistCount);
    }

    [Fact]
    public async Task Processor_runs_all_stages_to_completion_on_success()
    {
        var persistence = new RecordingPersistence();
        var processor = new VideoPipelineProcessor(
            persistence,
            [new NoOpStage(IngestionStageNames.Transcript), new NoOpStage(IngestionStageNames.Segments)]);
        var context = CreateContext();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.False(context.StageFailed);
        Assert.Equal(2, persistence.StageStatuses.Count(s => s.Status == IngestionStageStatuses.Completed));
        Assert.Empty(context.PendingEvents);
        Assert.Equal(1, persistence.PersistCount);
    }

    [Fact]
    public async Task Processor_sets_current_stage_on_item_as_it_progresses()
    {
        var persistence = new RecordingPersistence();
        var observedStages = new List<string>();
        var stage = new DelegateStage(IngestionStageNames.Segments, ctx => observedStages.Add(ctx.Item.Stage));
        var processor = new VideoPipelineProcessor(persistence, [stage]);
        var context = CreateContext();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.Equal([IngestionStageNames.Segments], observedStages);
    }

    private sealed class DelegateStage(string name, Action<VideoPipelineContext> action) : IVideoStageHandler
    {
        public string StageName => name;
        public Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
        {
            action(context);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Processor_marks_deferred_stage_deferred_instead_of_completed()
    {
        var persistence = new RecordingPersistence();
        var deferring = new DelegateStage(
            IngestionStageNames.Embeddings,
            ctx => ctx.DeferredStages.Add(IngestionStageNames.Embeddings));
        var processor = new VideoPipelineProcessor(persistence, [deferring]);
        var context = CreateContext();

        await processor.ProcessAsync(context, CancellationToken.None);

        Assert.False(context.StageFailed);
        Assert.Equal(
            [(IngestionStageNames.Embeddings, IngestionStageStatuses.Deferred)],
            persistence.StageStatuses);
    }

    // ── Idempotency guard ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("processed", false, VideoSkipReason.AlreadyProcessed)]
    [InlineData("processed_with_warnings", false, VideoSkipReason.AlreadyProcessed)]
    [InlineData("failed", false, VideoSkipReason.None)]
    [InlineData("processed", true, VideoSkipReason.None)]
    public void Idempotency_classifies_skip_reason(string? status, bool isReprocess, VideoSkipReason expected)
    {
        Assert.Equal(expected, VideoIdempotencyService.ClassifySkipReason(status, isReprocess));
    }
}

public sealed class TranscriptAndEmbeddingsStageTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    private static VideoPipelineContext CreateContext(Video? video = null)
    {
        var v = video ?? new Video(Guid.NewGuid(), "Test Video") { YoutubeVideoId = "abc12345678" };
        var run = new IngestionRun { Id = RunId, Status = "running" };
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = RunId,
            ItemType = "video",
            ItemId = v.Id,
            Stage = IngestionStageNames.Transcript,
            Status = "pending",
        };
        return new VideoPipelineContext { Video = v, Item = item, Run = run };
    }

    // ── Transcript stage: unavailable transcript is a warning, not a failure ─────

    [Fact]
    public async Task TranscriptStage_records_warning_and_no_transcript_when_unavailable()
    {
        var ingestion = new Moq.Mock<StreamingDigest.Application.Transcripts.ITranscriptIngestionService>();
        ingestion
            .Setup(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamingDigest.Application.Transcripts.TranscriptIngestionResult(
                Succeeded: false, TranscriptId: null, SourceType: null, LanguageCode: null,
                CueCount: 0, ErrorMessage: "unavailable_captions", Skipped: false));
        var dbContext = new Moq.Mock<StreamingDigest.Application.Transcripts.IStreamingDigestDbContext>();
        var context = CreateContext();
        var handler = new TranscriptStageHandler(
            ingestion.Object, dbContext.Object, NullLogger<TranscriptStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Null(context.Transcript);
        Assert.False(context.StageFailed);
        Assert.Contains(context.Warnings, w => w.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TranscriptStage_records_skipped_warning_for_short_videos()
    {
        var ingestion = new Moq.Mock<StreamingDigest.Application.Transcripts.ITranscriptIngestionService>();
        ingestion
            .Setup(s => s.IngestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StreamingDigest.Application.Transcripts.TranscriptIngestionResult(
                Succeeded: false, TranscriptId: null, SourceType: null, LanguageCode: null,
                CueCount: 0, ErrorMessage: "not_long_form", Skipped: true));
        var dbContext = new Moq.Mock<StreamingDigest.Application.Transcripts.IStreamingDigestDbContext>();
        var context = CreateContext();
        var handler = new TranscriptStageHandler(
            ingestion.Object, dbContext.Object, NullLogger<TranscriptStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains(context.Warnings, w => w.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    // ── Embeddings stage: guarded defer + reprocess notification ─────────────────

    private static GeneratedSearchDocument OneDocument(Guid videoId) => new(
        DocumentType: "video_metadata",
        SourceEntityType: "video",
        SourceEntityId: videoId,
        ParentVideoId: videoId,
        TitleEffective: "Test Video",
        BodyEffective: "body",
        ContentHash: "hash");

    [Fact]
    public async Task EmbeddingsStage_defers_storage_and_queues_reprocess_when_unready()
    {
        var context = CreateContext();
        var generator = new Moq.Mock<ISearchDocumentGenerator>();
        generator
            .Setup(g => g.Generate(It.IsAny<SearchDocumentGenerationRequest>()))
            .Returns([OneDocument(context.Video.Id)]);
        var store = new Moq.Mock<ISearchDocumentEmbeddingStore>(Moq.MockBehavior.Strict);
        var handler = new EmbeddingsStageHandler(
            generator.Object, store.Object, new UnreadyGuard(), NullLogger<EmbeddingsStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.ModelCapabilityUnready);
        var reprocess = Assert.Single(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.EmbeddingReprocessQueued);
        Assert.Equal("info", reprocess.Severity);
        Assert.Contains(context.Warnings, w => w.Contains("deferred", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(IngestionStageNames.Embeddings, context.DeferredStages);
        store.VerifyNoOtherCalls(); // nothing stored while unready
    }

    [Fact]
    public async Task EmbeddingsStage_stores_documents_when_ready()
    {
        var context = CreateContext();
        var generator = new Moq.Mock<ISearchDocumentGenerator>();
        generator
            .Setup(g => g.Generate(It.IsAny<SearchDocumentGenerationRequest>()))
            .Returns([OneDocument(context.Video.Id)]);
        var store = new Moq.Mock<ISearchDocumentEmbeddingStore>();
        store
            .Setup(s => s.DeleteForVideoScopeAsync(context.Video.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store
            .Setup(s => s.StoreAsync(
                It.IsAny<IEnumerable<GeneratedSearchDocument>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new EmbeddingsStageHandler(
            generator.Object, store.Object, new ReadyGuard(), NullLogger<EmbeddingsStageHandler>.Instance);

        await handler.ExecuteAsync(context, CancellationToken.None);

        store.Verify(s => s.StoreAsync(
            It.Is<IEnumerable<GeneratedSearchDocument>>(d => d.Count() == 1),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(context.PendingEvents, e => e.EventType == DomainEventTypeCatalog.EmbeddingReprocessQueued);
    }

    private sealed class UnreadyGuard : IModelReadinessGuard
    {
        public Task<bool> IsReadyAsync(string capability, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class ReadyGuard : IModelReadinessGuard
    {
        public Task<bool> IsReadyAsync(string capability, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
