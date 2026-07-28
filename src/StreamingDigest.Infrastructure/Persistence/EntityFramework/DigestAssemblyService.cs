using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class DigestAssemblyService(
    StreamingDigestDbContext context,
    IMatrixNotificationService? notificationService = null,
    INotificationDispatchService? notificationDispatchService = null)
{
    public async Task<Digest> AssembleAndPersistAsync(DigestAssemblyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = BuildPayload(request);
        var digestJson = DigestPayloadSerializer.Serialize(payload);

        var existingDigest = await context.Digests
            .SingleOrDefaultAsync(digest => digest.IngestionRunId == request.IngestionRunId, cancellationToken);

        Digest digestEntity;
        if (existingDigest is null)
        {
            digestEntity = new Digest(request.IngestionRunId, request.RunType)
            {
                PayloadJson = digestJson
            };

            context.Digests.Add(digestEntity);
        }
        else
        {
            existingDigest.RunType = request.RunType;
            existingDigest.PayloadJson = digestJson;
            digestEntity = existingDigest;
        }

        await context.SaveChangesAsync(cancellationToken);

        if (notificationDispatchService is not null)
        {
            await notificationDispatchService.QueueDigestNotificationAsync(digestEntity, request.OperationId, request.NotificationTarget, cancellationToken);
        }
        else if (notificationService is not null)
        {
            await notificationService.SendDigestSummaryAsync(digestEntity, cancellationToken);
        }

        return digestEntity;
    }

    private static DigestPayload BuildPayload(DigestAssemblyRequest request)
    {
        var evaluatedMatches = request.IsEmbeddingTransitionActive
            ? Array.Empty<HighSignalMatch>()
            : request.HighSignalMatches
                .Where(match => match.SimilarityPercent >= request.HighSignalThresholdPercent)
                .ToArray();

        return new DigestPayload
        {
            NewVideos = request.NewVideos.ToArray(),
            NewResources = request.NewResources.ToArray(),
            HighSignalMatches = evaluatedMatches,
            FailedItems = request.FailedItems.ToArray(),
            SkippedItems = request.SkippedItems.ToArray(),
            ActiveDeferments = request.ActiveDeferments.ToArray(),
            HighSignalEvaluationSkipped = request.IsEmbeddingTransitionActive,
            HighSignalThresholdPercent = request.HighSignalThresholdPercent,
            IsBackfillRun = request.IsBackfillRun
        };
    }
}

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
