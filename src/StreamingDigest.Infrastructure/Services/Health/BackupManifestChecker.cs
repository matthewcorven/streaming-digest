using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Services.Health;

namespace StreamingDigest.Infrastructure.Services.Health;

/// <summary>
/// Reads backup manifest files from the configured backup directory.
/// </summary>
public sealed class BackupManifestChecker : IBackupManifestChecker
{
    private readonly ApplicationConfiguration _configuration;
    private readonly ILogger<BackupManifestChecker> _logger;

    public BackupManifestChecker(
        ApplicationConfiguration configuration,
        ILogger<BackupManifestChecker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<BackupReadinessData> GetBackupReadinessAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking backup readiness from manifest");

        try
        {
            var backupDirectory = ResolveBackupDirectory();

            if (!Directory.Exists(backupDirectory))
            {
                _logger.LogDebug("Backup directory does not exist: {BackupDirectory}", backupDirectory);
                return new BackupReadinessData(
                    IsHealthy: false,
                    LastBackupAtUtc: null,
                    Status: "No backup directory",
                    Details: [$"Backup directory not found at: {backupDirectory}"]);
            }

            var backupFiles = Directory.EnumerateFiles(backupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .ToList();

            if (backupFiles.Count == 0)
            {
                _logger.LogDebug("No backup archives found in {BackupDirectory}", backupDirectory);
                return new BackupReadinessData(
                    IsHealthy: false,
                    LastBackupAtUtc: null,
                    Status: "No backups available",
                    Details: ["No backup archives found in configured backup directory."]);
            }

            // Read the latest backup
            var latestBackupPath = backupFiles.First();
            var backupData = await ReadBackupManifestAsync(latestBackupPath, cancellationToken);

            if (backupData is null)
            {
                _logger.LogWarning("Failed to read manifest from latest backup: {BackupPath}", latestBackupPath);
                return new BackupReadinessData(
                    IsHealthy: false,
                    LastBackupAtUtc: File.GetLastWriteTimeUtc(latestBackupPath),
                    Status: "Backup manifest unreadable",
                    Details: [$"Could not parse manifest from: {Path.GetFileName(latestBackupPath)}"]);
            }

            _logger.LogDebug(
                "Backup readiness check complete: {BackupFileName}, created: {CreatedAt}, status: {VerificationStatus}",
                Path.GetFileName(latestBackupPath),
                backupData.CreatedAtUtc,
                backupData.VerificationStatus);

            var isHealthy = backupData.VerificationStatus == "completed" || backupData.VerificationStatus == "verified";
            return new BackupReadinessData(
                IsHealthy: isHealthy,
                LastBackupAtUtc: TryParseIso8601(backupData.CreatedAtUtc),
                Status: backupData.VerificationStatus == "completed" || backupData.VerificationStatus == "verified"
                    ? "Backup verified"
                    : $"Backup {backupData.VerificationStatus}",
                Details:
                [
                    $"Backup: {Path.GetFileName(latestBackupPath)}",
                    $"Schema version: {backupData.SchemaVersion}",
                    $"Restore target: {backupData.RestoreTarget}",
                    $"Assets: {backupData.Assets.Count}"
                ]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking backup readiness");
            return new BackupReadinessData(
                IsHealthy: false,
                LastBackupAtUtc: null,
                Status: "Error reading backup status",
                Details: [ex.Message]);
        }
    }

    private async Task<BackupManifestData?> ReadBackupManifestAsync(string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            using var zipArchive = ZipFile.OpenRead(backupPath);
            var manifestEntry = zipArchive.Entries.FirstOrDefault(e => e.Name == "manifest.json");

            if (manifestEntry is null)
            {
                _logger.LogWarning("No manifest.json found in backup archive: {BackupPath}", backupPath);
                return null;
            }

            using var manifestStream = manifestEntry.Open();
            var manifestData = await JsonSerializer.DeserializeAsync<BackupManifestData>(
                manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            return manifestData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read manifest from backup: {BackupPath}", backupPath);
            return null;
        }
    }

    private string ResolveBackupDirectory()
    {
        var configuredPath = _configuration.Backup.DestinationPath;

        // If path is absolute, use it directly
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        // If relative, resolve relative to current directory
        return Path.GetFullPath(configuredPath);
    }

    private static DateTime? TryParseIso8601(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return null;
        }

        if (DateTime.TryParse(dateString, out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    /// <summary>
    /// Backup manifest structure from backup archives.
    /// </summary>
    private sealed class BackupManifestData
    {
        public string CreatedAtUtc { get; init; } = string.Empty;

        public string BackupFileName { get; init; } = string.Empty;

        public string SchemaVersion { get; init; } = string.Empty;

        public string VerificationStatus { get; init; } = "pending";

        public string RestoreTarget { get; init; } = string.Empty;

        public List<BackupAssetData> Assets { get; init; } = [];
    }

    /// <summary>
    /// Asset within a backup manifest.
    /// </summary>
    private sealed record BackupAssetData(
        string Name,
        string Status,
        string? Path,
        string? Details);
}
