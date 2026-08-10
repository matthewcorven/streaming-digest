using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Assembles the run-scoped Digest payload from pipeline outcomes and persists it,
/// then enqueues the notification outbox so Matrix and the dashboard agree (ADR-0006,
/// plan §4.7 / §10.4).
/// </summary>
public interface IDigestAssemblyService
{
    /// <summary>
    /// Assembles + persists a <see cref="Digest"/> for the given request, queues the
    /// notification outbox entry, and returns the persisted entity.
    /// </summary>
    Task<Digest> AssembleAndPersistAsync(DigestAssemblyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// All data required to assemble one run-scoped digest.  Constructed by the orchestrator
/// at run completion and handed to <see cref="IDigestAssemblyService"/>.
/// </summary>
public sealed class DigestAssemblyRequest
{
    public Guid IngestionRunId { get; init; }
    public Guid? OperationId { get; init; }
    public string RunType { get; init; } = "standard";
    public string? NotificationTarget { get; init; }
    public IReadOnlyCollection<DigestItem> NewVideos { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyCollection<DigestResource> NewResources { get; init; } = Array.Empty<DigestResource>();
    public IReadOnlyCollection<HighSignalMatch> HighSignalMatches { get; init; } = Array.Empty<HighSignalMatch>();
    public IReadOnlyCollection<DigestItem> FailedItems { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyCollection<DigestItem> SkippedItems { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyCollection<ActiveDeferment> ActiveDeferments { get; init; } = Array.Empty<ActiveDeferment>();
    public bool IsEmbeddingTransitionActive { get; init; }
    public bool IsBackfillRun { get; init; }
    public double HighSignalThresholdPercent { get; init; } = 70d;
}
