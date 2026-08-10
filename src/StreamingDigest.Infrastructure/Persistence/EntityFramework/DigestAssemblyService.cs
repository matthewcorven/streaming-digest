using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class DigestAssemblyService(
    StreamingDigestDbContext context,
    IMatrixNotificationService? notificationService = null,
    INotificationDispatchService? notificationDispatchService = null) : IDigestAssemblyService
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
