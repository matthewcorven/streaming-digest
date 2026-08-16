namespace StreamingDigest.Application.Services.Health;

/// <summary>
/// Checks backup manifest health and readiness status.
/// </summary>
public interface IBackupManifestChecker
{
    /// <summary>
    /// Gets backup readiness information from actual backup metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Backup readiness data including last backup timestamp and status.</returns>
    Task<BackupReadinessData> GetBackupReadinessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contains backup readiness information retrieved from manifest.
/// </summary>
public sealed record BackupReadinessData(
    bool IsHealthy,
    DateTime? LastBackupAtUtc,
    string Status,
    List<string> Details)
{
    /// <summary>Time since last backup in human-readable format (e.g., "2h ago", "Never").</summary>
    public string TimeSinceLastBackup
    {
        get
        {
            if (LastBackupAtUtc is null)
            {
                return "Never";
            }

            var elapsed = DateTime.UtcNow - LastBackupAtUtc.Value;
            return elapsed.TotalMinutes < 1
                ? "Just now"
                : elapsed.TotalHours < 1
                    ? $"{(int)elapsed.TotalMinutes}m ago"
                    : elapsed.TotalDays < 1
                        ? $"{(int)elapsed.TotalHours}h ago"
                        : $"{(int)elapsed.TotalDays}d ago";
        }
    }
}
