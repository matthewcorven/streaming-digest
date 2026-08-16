using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<BackupManifestChecker> _logger;

    public BackupManifestChecker(
        ApplicationConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<BackupManifestChecker> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
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
                    IsError: true,
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
                    IsError: false,
                    LastBackupAtUtc: null,
                    Status: "No backups available",
                    Details: ["No backup archives found in configured backup directory."]);
            }

            // Check retention policy
            var maxAgeHours = _configuration.Backup.MaxAgeHours ?? 72; // Default 3 days
            var minimumBackupCount = _configuration.Backup.MinimumBackupCount ?? 1;

            var latestBackupPath = backupFiles.First();
            var latestBackupTime = File.GetLastWriteTimeUtc(latestBackupPath);
            var backupAge = DateTime.UtcNow - latestBackupTime;

            _logger.LogDebug("Latest backup age: {BackupAge}h, max policy: {MaxAge}h, minimum count policy: {MinCount}",
                backupAge.TotalHours, maxAgeHours, minimumBackupCount);

            // Read the latest backup
            var backupData = await ReadBackupManifestAsync(latestBackupPath, cancellationToken);

            if (backupData is null)
            {
                _logger.LogDebug("Failed to read manifest from latest backup: {BackupPath}", latestBackupPath);
                return new BackupReadinessData(
                    IsHealthy: false,
                    IsError: false,
                    LastBackupAtUtc: latestBackupTime,
                    Status: "Backup manifest unreadable",
                    Details: [$"Could not parse manifest from: {Path.GetFileName(latestBackupPath)}"]);
            }

            _logger.LogDebug(
                "Backup readiness check complete: {BackupFileName}, created: {CreatedAt}, status: {VerificationStatus}",
                Path.GetFileName(latestBackupPath),
                backupData.CreatedAtUtc,
                backupData.VerificationStatus);

            // Only "verified" status indicates healthy backup; "completed" is awaiting verification
            var isVerified = backupData.VerificationStatus == "verified";
            var retentionCompliant = backupAge.TotalHours <= maxAgeHours && backupFiles.Count >= minimumBackupCount;
            var isHealthy = isVerified && retentionCompliant;

            _logger.LogDebug("Backup health determination: verified={IsVerified}, retentionCompliant={RetentionCompliant}, healthy={IsHealthy}",
                isVerified, retentionCompliant, isHealthy);

            return new BackupReadinessData(
                IsHealthy: isHealthy,
                IsError: false,
                LastBackupAtUtc: TryParseIso8601(backupData.CreatedAtUtc),
                Status: isHealthy
                    ? "Backup verified"
                    : !isVerified
                        ? $"Backup {backupData.VerificationStatus} (awaiting verification)"
                        : "Backup verification complete but retention policy not met",
                Details:
                [
                    $"Backup: {Path.GetFileName(latestBackupPath)}",
                    $"Schema version: {backupData.SchemaVersion}",
                    $"Verification status: {backupData.VerificationStatus}",
                    $"Backup age: {backupAge.TotalHours:F1}h (policy: max {maxAgeHours}h)",
                    $"Backup count: {backupFiles.Count} (policy: min {minimumBackupCount})",
                    $"Restore target: {backupData.RestoreTarget}",
                    $"Assets: {backupData.Assets.Count}"
                ]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking backup readiness");
            return new BackupReadinessData(
                IsHealthy: false,
                IsError: true,
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
                _logger.LogDebug("No manifest.json found in backup archive: {BackupPath}", backupPath);
                return null;
            }

            using var manifestStream = manifestEntry.Open();
            var manifestData = await JsonSerializer.DeserializeAsync<BackupManifestData>(
                manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            // Validate required fields and schema version
            if (manifestData is null)
            {
                _logger.LogDebug("Manifest deserialization returned null");
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifestData.CreatedAtUtc))
            {
                _logger.LogDebug("Manifest validation failed: CreatedAtUtc is missing");
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifestData.SchemaVersion))
            {
                _logger.LogDebug("Manifest validation failed: SchemaVersion is missing");
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifestData.VerificationStatus))
            {
                _logger.LogDebug("Manifest validation failed: VerificationStatus is missing");
                return null;
            }

            _logger.LogDebug("Manifest schema validation complete: version={SchemaVersion}, status={VerificationStatus}", 
                manifestData.SchemaVersion, manifestData.VerificationStatus);

            return manifestData;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read manifest from backup: {BackupPath}", backupPath);
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

        // If relative, resolve relative to app content root, not CWD
        var contentRoot = _hostEnvironment.ContentRootPath;
        return Path.Combine(contentRoot, configuredPath);
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
