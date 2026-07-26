namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public interface IRetentionCleanupService
{
    Task<RetentionCleanupResult> RunAsync(CancellationToken cancellationToken = default);

    Task<MediaArtifactPurgeResult> PurgeOwnedArtifactsAsync(
        string ownerType,
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken = default);
}

public sealed record RetentionCleanupResult(
    int RetentionDays,
    bool RetentionEnabled,
    int DeletedDomainEventCount);

public sealed record MediaArtifactPurgeResult(
    int DeletedArtifactRecordCount,
    int DeletedFileCount);
