using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class RetentionCleanupService(
    StreamingDigestDbContext context,
    ILogger<RetentionCleanupService> logger) : IRetentionCleanupService
{
    private const string RetentionSettingKey = "observability.retentionDays";

    public async Task<RetentionCleanupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = await ReadRetentionDaysAsync(cancellationToken);
        if (retentionDays <= 0)
        {
            logger.LogInformation("Skipping retention cleanup because {SettingKey} is disabled.", RetentionSettingKey);
            return new RetentionCleanupResult(retentionDays, false, 0);
        }

        var deletedDomainEventCount = await DeleteExpiredDomainEventsAsync(retentionDays, DateTimeOffset.UtcNow, cancellationToken);
        logger.LogInformation(
            "Retention cleanup deleted {DeletedDomainEventCount} domain events older than {RetentionDays} days.",
            deletedDomainEventCount,
            retentionDays);

        return new RetentionCleanupResult(retentionDays, true, deletedDomainEventCount);
    }

    public async Task<MediaArtifactPurgeResult> PurgeOwnedArtifactsAsync(
        string ownerType,
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);
        ArgumentNullException.ThrowIfNull(ownerIds);

        if (ownerIds.Count == 0)
        {
            return new MediaArtifactPurgeResult(0, 0);
        }

        var artifacts = await context.MediaArtifacts
            .Where(artifact => artifact.OwnerType == ownerType && ownerIds.Contains(artifact.OwnerId))
            .ToListAsync(cancellationToken);

        var deletedFileCount = 0;
        foreach (var artifact in artifacts)
        {
            if (File.Exists(artifact.FilePath))
            {
                File.Delete(artifact.FilePath);
                deletedFileCount += 1;
            }
        }

        context.MediaArtifacts.RemoveRange(artifacts);
        await context.SaveChangesAsync(cancellationToken);

        return new MediaArtifactPurgeResult(artifacts.Count, deletedFileCount);
    }

    public async Task<int> DeleteExpiredDomainEventsAsync(int retentionDays, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionDays);
        var cutoff = now.AddDays(-retentionDays);

        var expiredEvents = (await context.DomainEvents.ToListAsync(cancellationToken))
            .Where(domainEvent => domainEvent.CreatedAt < cutoff)
            .ToList();

        context.DomainEvents.RemoveRange(expiredEvents);
        await context.SaveChangesAsync(cancellationToken);
        return expiredEvents.Count;
    }

    private async Task<int> ReadRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value_json::text FROM public.app_settings WHERE key = @key LIMIT 1";

            var keyParameter = command.CreateParameter();
            keyParameter.ParameterName = "@key";
            keyParameter.Value = RetentionSettingKey;
            command.Parameters.Add(keyParameter);

            var rawValue = await command.ExecuteScalarAsync(cancellationToken);
            return rawValue switch
            {
                string stringValue when int.TryParse(stringValue, out var retentionDays) => retentionDays,
                _ => 0
            };
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
