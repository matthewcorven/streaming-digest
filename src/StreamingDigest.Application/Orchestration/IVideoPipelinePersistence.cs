namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Scope-bound persistence surface used by the per-video pipeline. One instance exists per
/// video DI scope; implementations wrap that scope's DbContext + A1 repositories so the
/// pipeline remains testable without a real database.
/// </summary>
public interface IVideoPipelinePersistence
{
    /// <summary>
    /// Persists everything the pipeline accumulated on the context: segment generation,
    /// external resources, repository records, scraped pages, and pending domain events.
    /// </summary>
    Task PersistPipelineChangesAsync(VideoPipelineContext context, CancellationToken cancellationToken);

    /// <summary>Writes one per-stage status cell on the item row.</summary>
    Task SetStageStatusAsync(Guid itemId, string stageName, string status, CancellationToken cancellationToken);

    /// <summary>Writes the item's terminal status and error summary.</summary>
    Task FinalizeItemAsync(Guid itemId, string status, string? errorSummary, CancellationToken cancellationToken);

    /// <summary>Updates the video row's ingestion status after the pipeline finishes.</summary>
    Task SetVideoIngestionStatusAsync(Guid videoId, string status, Guid? runId, bool succeeded, CancellationToken cancellationToken);
}
