using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Stages 7+8 — search-document generation and embeddings. Generates search
/// documents from everything upstream stages produced (video metadata, segments,
/// transcript chunks, link metadata, scraped pages, repository readmes), then
/// embeds and stores them. Embedding is guarded: when the embeddings capability is
/// unready, storage is deferred (documents are still generated; a notification
/// event is emitted exactly once) so a later reprocess can fill in embeddings.
/// </summary>
public sealed class EmbeddingsStageHandler(
    ISearchDocumentGenerator searchDocumentGenerator,
    ISearchDocumentEmbeddingStore searchDocumentEmbeddingStore,
    IModelReadinessGuard modelReadinessGuard,
    ILogger<EmbeddingsStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Embeddings;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        var request = BuildRequest(context);
        var documents = searchDocumentGenerator.Generate(request);
        if (documents.Count == 0)
        {
            logger.LogDebug("Video {VideoId}: no search documents generated", context.Video.YoutubeVideoId);
            return;
        }

        var embeddingsReady = await modelReadinessGuard.IsReadyAsync(ModelCapabilities.Embeddings, cancellationToken);
        if (!embeddingsReady)
        {
            context.Warnings.Add("embeddings: embedding capability unready; deferred");
            context.DeferredStages.Add(StageName);
            context.PendingEvents.Add(StageNotification.CapabilityUnready(
                ModelCapabilities.Embeddings, StageName, "embedding storage deferred", context));
            context.PendingEvents.Add(new DomainEvent
            {
                EventType = DomainEventTypeCatalog.EmbeddingReprocessQueued,
                Severity = "info",
                EntityType = "video",
                EntityId = context.Video.Id,
                IngestionRunId = context.Run.Id,
                Message = $"Embeddings deferred for video '{context.Video.YoutubeVideoId}'; reprocess queued.",
            });
            return;
        }

        await searchDocumentEmbeddingStore.DeleteForVideoScopeAsync(context.Video.Id, cancellationToken);
        var stored = await searchDocumentEmbeddingStore.StoreAsync(
            documents,
            generatedByOperationId: context.Run.OperationId,
            cancellationToken);

        logger.LogDebug(
            "Video {VideoId}: stored {Count} search document embeddings",
            context.Video.YoutubeVideoId, stored.Count);
    }

    private static SearchDocumentGenerationRequest BuildRequest(VideoPipelineContext context)
    {
        var video = context.Video;
        return new SearchDocumentGenerationRequest
        {
            ParentVideoId = video.Id,
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(
                    video.Id,
                    video.Title,
                    video.TitleOverride,
                    video.DescriptionOriginal,
                    video.DescriptionOverride),
            ],
            SegmentTitlesAndSummaries = (context.SegmentGeneration?.Segments ?? [])
                .Select(s => (SegmentTitleSummaryDocumentInput)new(
                    s.Id, s.TitleOriginal, s.TitleOverride, s.SummaryOriginal, s.SummaryOverride))
                .ToList(),
            TranscriptChunks = (context.Transcript?.Cues ?? (IEnumerable<TranscriptCue>)[])
                .Select((cue, index) => new TranscriptChunkDocumentInput(cue.Id, cue.TextOriginal, cue.TextOverride, index))
                .ToList(),
            ExternalLinkMetadata = context.Resources
                .Select(r => new ExternalLinkMetadataDocumentInput(
                    r.Id, r.TitleOriginal, r.TitleOverride, r.DescriptionOriginal, r.DescriptionOverride,
                    r.ClassificationOriginal, r.ClassificationOverride, r.Domain, DomainOverride: null))
                .ToList(),
            ScrapedPageText = context.ScrapedPages
                .Where(p => p.ScrapeStatus == "succeeded")
                .Select(p => new ScrapedPageTextDocumentInput(
                    p.Id, p.TitleOriginal, p.TitleOverride, p.DescriptionOriginal, p.DescriptionOverride,
                    p.VisibleTextOriginal, p.VisibleTextOverride))
                .ToList(),
            RepositoryReadmeChunks = [],
            Notes = [],
        };
    }
}
