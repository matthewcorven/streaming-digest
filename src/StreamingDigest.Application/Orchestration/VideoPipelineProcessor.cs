using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Executes the per-video guarded pipeline for one <see cref="VideoPipelineContext"/>
/// against a scope-bound set of services. Created fresh per video by the orchestrator
/// (one DI scope per video) so scoped DbContexts/repositories are never shared across
/// concurrent video pipelines.
/// </summary>
public sealed class VideoPipelineProcessor(
    IVideoPipelinePersistence persistence,
    IEnumerable<IVideoStageHandler> stages)
{
    private readonly IReadOnlyList<IVideoStageHandler> stages = stages.ToArray();

    public async Task ProcessAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Item.Stage = stage.StageName;

            try
            {
                await stage.ExecuteAsync(context, cancellationToken);
                var status = context.DeferredStages.Contains(stage.StageName)
                    ? IngestionStageStatuses.Deferred
                    : IngestionStageStatuses.Completed;
                await persistence.SetStageStatusAsync(context.Item.Id, stage.StageName, status, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A stage-level unexpected failure fails that stage and the item, but
                // never the run: the failure is recorded + notified, and the run moves
                // on to the next video.
                context.Warnings.Add($"{stage.StageName}: unexpected failure: {ex.Message}");
                context.StageFailed = true;
                await persistence.SetStageStatusAsync(context.Item.Id, stage.StageName, IngestionStageStatuses.Failed, cancellationToken);
                context.PendingEvents.Add(new DomainEvent
                {
                    EventType = DomainEventTypeCatalog.IngestionStageFailed,
                    Severity = "error",
                    EntityType = "video",
                    EntityId = context.Video.Id,
                    IngestionRunId = context.Run.Id,
                    Message = $"Stage '{stage.StageName}' failed for video '{context.Video.YoutubeVideoId}': {ex.Message}",
                });
                await persistence.PersistPipelineChangesAsync(context, cancellationToken);
                return;
            }
        }

        await persistence.PersistPipelineChangesAsync(context, cancellationToken);
    }
}
