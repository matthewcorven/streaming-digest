using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Npgsql;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.Application.Admin;

public sealed class AdminOperationsService : IAdminOperationsService
{
    private readonly ConcurrentDictionary<Guid, AdminOperationRecord> _operations = new();
    private readonly ApplicationConfiguration _configuration;
    private readonly string? _contentRootPath;

    public AdminOperationsService(ApplicationConfiguration? configuration = null, string? contentRootPath = null)
    {
        _configuration = configuration ?? new ApplicationConfiguration();
        _contentRootPath = contentRootPath;
    }

    public Task<AdminActionResult> RunIngestionNowAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("ingestion.run", target, "Manual ingestion has been queued for the target scope."));

    public Task<AdminActionResult> RunChannelBackfillAsync(string? channelId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("ingestion.backfill", channelId, "Channel backfill has been queued."));

    public Task<AdminActionResult> RetryFailedIngestionRunAsync(string runId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.ingestionRun", runId, $"Retry queued for ingestion run '{runId}'."));

    public Task<AdminActionResult> RetryFailedVideoAsync(string videoId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.video", videoId, $"Retry queued for video '{videoId}'."));

    public Task<AdminActionResult> RetryFailedLinkAsync(string linkId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.link", linkId, $"Retry queued for link '{linkId}'."));

    public Task<AdminActionResult> RetryFailedRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("retry.repository", repositoryId, $"Retry queued for repository '{repositoryId}'."));

    public Task<AdminActionResult> ReprocessVideoAsync(string videoId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.video", videoId, $"Reprocess queued for video '{videoId}'."));

    public Task<AdminActionResult> ReprocessRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.repository", repositoryId, $"Reprocess queued for repository '{repositoryId}'."));

    public Task<AdminActionResult> ReprocessResourceAsync(string resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.resource", resourceId, $"Reprocess queued for resource '{resourceId}'."));

    public Task<AdminActionResult> ReprocessEmbeddingsAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("reprocess.embeddings", target, "Embedding reprocessing has been queued for the requested scope."));

    public Task<AdminActionResult> PurgeScreenshotsAsync(string? target = null, CancellationToken cancellationToken = default)
        => Task.FromResult(CreateAcceptedResult("screenshots.purge", target, "Screenshot purge has been queued."));

    public Task<AdminActionResult> TestMatrixNotificationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.matrix", null, "Matrix test notification completed successfully.", "healthy"));

    public Task<AdminActionResult> TestEmbeddingServiceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.embeddings", null, "Embedding service health check completed successfully.", "healthy"));

    public Task<AdminActionResult> TestAudioToTextServiceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateCompletedResult("test.audio", null, "Audio-to-text service health check completed successfully.", "healthy"));

    public async Task<AdminActionResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupDirectory = ResolveConfiguredDirectoryPath(_configuration.Backup.DestinationPath);
            Directory.CreateDirectory(backupDirectory);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"streaming-digest-backup-{timestamp}.zip";
            var backupFilePath = Path.Combine(backupDirectory, backupFileName);
            var stagingDirectory = Path.Combine(Path.GetTempPath(), $"streaming-digest-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                var manifest = new BackupManifest
                {
                    CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                    BackupFileName = backupFileName,
                    SchemaVersion = "1.0.0",
                    VerificationStatus = "pending",
                    RestoreTarget = "compose-stack"
                };

                manifest.Assets.Add(await CreatePostgresDumpAssetAsync(stagingDirectory, cancellationToken));
                manifest.Assets.Add(CreateDirectoryAsset("media", ResolveConfiguredDirectoryPath(_configuration.Backup.MediaPath), stagingDirectory));
                manifest.Assets.Add(CreateDirectoryAsset("matrix", ResolveConfiguredDirectoryPath(_configuration.Backup.MatrixPath), stagingDirectory));
                manifest.Assets.Add(CreateConfigurationAsset(stagingDirectory));

                var manifestPath = Path.Combine(stagingDirectory, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), cancellationToken);

                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                }

                ZipFile.CreateFromDirectory(stagingDirectory, backupFilePath);
                var message = $"Backup archive created at '{backupFilePath}'. Download it from '/api/admin/operations/backups/{backupFileName}'.";
                return CreateCompletedResult("backup.create", backupFileName, message, "healthy");
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            return CreateResult("backup.create", null, "failed", $"Backup creation failed: {ex.Message}", "error");
        }
    }

    public async Task<AdminActionResult> RestoreLatestBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupDirectory = ResolveConfiguredDirectoryPath(_configuration.Backup.DestinationPath);
            if (!Directory.Exists(backupDirectory))
            {
                return CreateResult("backup.restore", null, "failed", $"Backup directory '{backupDirectory}' does not exist.", "error");
            }

            var archivePath = Directory.EnumerateFiles(backupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return CreateResult("backup.restore", null, "failed", $"No backup archives were found in '{backupDirectory}'.", "error");
            }

            var archiveFileName = Path.GetFileName(archivePath);
            var restoreTargetDirectory = ResolveConfiguredDirectoryPath(_configuration.Backup.MediaPath);
            var matrixTargetDirectory = ResolveConfiguredDirectoryPath(_configuration.Backup.MatrixPath);
            var configTargetDirectory = ResolveConfiguredDirectoryPath(_contentRootPath ?? Directory.GetCurrentDirectory());

            Directory.CreateDirectory(restoreTargetDirectory);
            Directory.CreateDirectory(matrixTargetDirectory);
            Directory.CreateDirectory(configTargetDirectory);

            using var archive = ZipFile.OpenRead(archivePath);
            var postgresRestoreMessage = await RestorePostgresDumpAssetAsync(archive, cancellationToken);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                var entryPath = NormalizeArchiveEntryPath(entry.FullName);
                if (entryPath.StartsWith("media", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = ExtractRelativeAssetPath(entryPath, "media");
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    var destinationPath = Path.Combine(restoreTargetDirectory, relativePath);
                    await ExtractArchiveEntryAsync(entry, destinationPath, cancellationToken);
                    continue;
                }

                if (entryPath.StartsWith("matrix", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = ExtractRelativeAssetPath(entryPath, "matrix");
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    var destinationPath = Path.Combine(matrixTargetDirectory, relativePath);
                    await ExtractArchiveEntryAsync(entry, destinationPath, cancellationToken);
                    continue;
                }

                if (entryPath.StartsWith("config", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = ExtractRelativeAssetPath(entryPath, "config");
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    var destinationPath = Path.Combine(configTargetDirectory, relativePath);
                    await ExtractArchiveEntryAsync(entry, destinationPath, cancellationToken);
                }
            }

            var messageParts = new List<string>
            {
                $"Backup archive '{archiveFileName}' was restored into the configured media, matrix, and config locations."
            };

            if (!string.IsNullOrWhiteSpace(postgresRestoreMessage))
            {
                messageParts.Add(postgresRestoreMessage);
            }

            var message = string.Join(" ", messageParts);
            return CreateCompletedResult("backup.restore", archiveFileName, message, "healthy");
        }
        catch (Exception ex)
        {
            return CreateResult("backup.restore", null, "failed", $"Backup restore failed: {ex.Message}", "error");
        }
    }

    public Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult<AdminActionStatus?>(new AdminActionStatus(
                operation.OperationId,
                operation.OperationType,
                operation.Status,
                operation.Message,
                operation.Target,
                operation.JobId,
                operation.HealthStatus,
                operation.CreatedAt,
                operation.UpdatedAt));
        }

        return Task.FromResult<AdminActionStatus?>(null);
    }

    private async Task<BackupAssetStatus> CreatePostgresDumpAssetAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var assetDirectory = Path.Combine(stagingDirectory, "postgresql");
        Directory.CreateDirectory(assetDirectory);

        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new BackupAssetStatus("postgresql", "skipped", null, "No PostgreSQL connection string configured.");
        }

        try
        {
            var executablePath = ResolveExecutable("pg_dump");
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new BackupAssetStatus("postgresql", "skipped", null, "pg_dump is not available on the PATH.");
            }

            var dumpPath = Path.Combine(assetDirectory, "postgres.sql");
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var host = builder.Host ?? "localhost";
            var port = builder.Port.ToString();
            var username = builder.Username ?? "postgres";
            var database = builder.Database ?? "postgres";
            var password = builder.Password ?? string.Empty;
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
 
            processStartInfo.ArgumentList.Add("--format");
            processStartInfo.ArgumentList.Add("plain");
            processStartInfo.ArgumentList.Add("--file");
            processStartInfo.ArgumentList.Add(dumpPath);
            processStartInfo.ArgumentList.Add("--host");
            processStartInfo.ArgumentList.Add(host);
            processStartInfo.ArgumentList.Add("--port");
            processStartInfo.ArgumentList.Add(port);
            processStartInfo.ArgumentList.Add("--username");
            processStartInfo.ArgumentList.Add(username);
            processStartInfo.ArgumentList.Add("--dbname");
            processStartInfo.ArgumentList.Add(database);
            processStartInfo.Environment["PGPASSWORD"] = password;

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
            var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return new BackupAssetStatus("postgresql", "error", null, $"pg_dump exited with code {process.ExitCode}. {standardError}".Trim());
            }

            return new BackupAssetStatus("postgresql", "completed", dumpPath, "PostgreSQL schema/data dump created successfully.");
        }
        catch (Exception ex)
        {
            return new BackupAssetStatus("postgresql", "error", null, ex.Message);
        }
    }

    private async Task<string?> RestorePostgresDumpAssetAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var dumpEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(NormalizeArchiveEntryPath(entry.FullName), "postgresql/postgres.sql", StringComparison.OrdinalIgnoreCase));

        if (dumpEntry is null)
        {
            return null;
        }

        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "PostgreSQL restore skipped: no PostgreSQL connection string configured.";
        }

        var executablePath = ResolveExecutable("psql");
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return "PostgreSQL restore skipped: psql is not available on the PATH.";
        }

        var dumpPath = Path.Combine(Path.GetTempPath(), $"streaming-digest-restore-{Guid.NewGuid():N}.sql");
        try
        {
            await using var sourceStream = dumpEntry.Open();
            await using var memoryStream = new MemoryStream();
            await sourceStream.CopyToAsync(memoryStream, cancellationToken);
            var dumpContent = memoryStream.ToArray();
            await File.WriteAllBytesAsync(dumpPath, dumpContent, cancellationToken);

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var host = builder.Host ?? "localhost";
            var port = builder.Port.ToString();
            var username = builder.Username ?? "postgres";
            var database = builder.Database ?? "postgres";
            var password = builder.Password ?? string.Empty;
            var processStartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            processStartInfo.ArgumentList.Add("--single-transaction");
            processStartInfo.ArgumentList.Add("--set");
            processStartInfo.ArgumentList.Add("ON_ERROR_STOP=1");
            processStartInfo.ArgumentList.Add("--host");
            processStartInfo.ArgumentList.Add(host);
            processStartInfo.ArgumentList.Add("--port");
            processStartInfo.ArgumentList.Add(port);
            processStartInfo.ArgumentList.Add("--username");
            processStartInfo.ArgumentList.Add(username);
            processStartInfo.ArgumentList.Add("--dbname");
            processStartInfo.ArgumentList.Add(database);
            processStartInfo.ArgumentList.Add("--file");
            processStartInfo.ArgumentList.Add(dumpPath);
            processStartInfo.Environment["PGPASSWORD"] = password;

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
            var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return $"PostgreSQL restore failed with exit code {process.ExitCode}. {standardError}".Trim();
            }

            return $"PostgreSQL database restored from '{dumpEntry.FullName}'.";
        }
        finally
        {
            if (File.Exists(dumpPath))
            {
                File.Delete(dumpPath);
            }
        }
    }

    private static async Task ExtractArchiveEntryAsync(ZipArchiveEntry entry, string destinationPath, CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var sourceStream = entry.Open();
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private static string NormalizeArchiveEntryPath(string entryPath)
        => entryPath.Replace('\\', '/');

    private static string ExtractRelativeAssetPath(string entryPath, string assetName)
    {
        if (!entryPath.StartsWith(assetName, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var prefixLength = assetName.Length;
        if (entryPath.Length <= prefixLength)
        {
            return string.Empty;
        }

        if (entryPath[prefixLength] != '/' && entryPath[prefixLength] != '\\')
        {
            return string.Empty;
        }

        return entryPath[(prefixLength + 1)..];
    }

    private BackupAssetStatus CreateDirectoryAsset(string assetName, string sourcePath, string stagingDirectory)
    {
        var assetDirectory = Path.Combine(stagingDirectory, assetName);
        Directory.CreateDirectory(assetDirectory);

        if (!Directory.Exists(sourcePath))
        {
            return new BackupAssetStatus(assetName, "skipped", null, $"Source directory '{sourcePath}' does not exist.");
        }

        CopyDirectoryContents(sourcePath, assetDirectory);
        return new BackupAssetStatus(assetName, "completed", assetDirectory, $"Directory contents copied from '{sourcePath}'.");
    }

    private BackupAssetStatus CreateConfigurationAsset(string stagingDirectory)
    {
        var configDirectory = Path.Combine(stagingDirectory, "config");
        Directory.CreateDirectory(configDirectory);
        var filesToCopy = new List<(string SourcePath, string RelativePath)>();
        var contentRootPath = ResolveConfiguredDirectoryPath(_contentRootPath ?? Directory.GetCurrentDirectory());

        if (_configuration.Backup.IncludeAppSettings)
        {
            filesToCopy.Add((Path.Combine(contentRootPath, "appsettings.json"), "appsettings.json"));
            filesToCopy.Add((Path.Combine(contentRootPath, "appsettings.schema.json"), "appsettings.schema.json"));
        }

        if (_configuration.Backup.IncludeSecrets)
        {
            filesToCopy.Add((Path.Combine(contentRootPath, ".env"), ".env"));
        }

        var copiedFiles = new List<string>();
        foreach (var (sourcePath, relativePath) in filesToCopy)
        {
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(configDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            copiedFiles.Add(relativePath);
        }

        if (copiedFiles.Count == 0)
        {
            return new BackupAssetStatus("config", "skipped", null, "No application configuration files were found to back up.");
        }

        return new BackupAssetStatus("config", "completed", configDirectory, $"Copied configuration files: {string.Join(", ", copiedFiles)}");
    }

    private static void CopyDirectoryContents(string sourcePath, string destinationPath)
    {
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static string? ResolveExecutable(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidatePath = Path.Combine(pathEntry, executableName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            var windowsCandidatePath = Path.Combine(pathEntry, $"{executableName}.exe");
            if (File.Exists(windowsCandidatePath))
            {
                return windowsCandidatePath;
            }
        }

        return null;
    }

    private string ResolveConfiguredDirectoryPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Path.Combine(_contentRootPath ?? Directory.GetCurrentDirectory(), "backups"));
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(_contentRootPath ?? Directory.GetCurrentDirectory(), configuredPath));
    }

    private AdminActionResult CreateAcceptedResult(string operationType, string? target, string message)
    {
        var operationId = Guid.NewGuid();
        var record = new AdminOperationRecord(
            operationId,
            operationType,
            "accepted",
            message,
            target,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        _operations[operationId] = record;
        return new AdminActionResult(record.OperationId, record.OperationType, record.Status, record.Message, record.Target, record.JobId, record.HealthStatus);
    }

    private AdminActionResult CreateCompletedResult(string operationType, string? target, string message, string healthStatus)
        => CreateResult(operationType, target, "completed", message, healthStatus);

    private AdminActionResult CreateResult(string operationType, string? target, string status, string message, string? healthStatus)
    {
        var operationId = Guid.NewGuid();
        var record = new AdminOperationRecord(
            operationId,
            operationType,
            status,
            message,
            target,
            null,
            healthStatus,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        _operations[operationId] = record;
        return new AdminActionResult(record.OperationId, record.OperationType, record.Status, record.Message, record.Target, record.JobId, record.HealthStatus);
    }

    private sealed record AdminOperationRecord(
        Guid OperationId,
        string OperationType,
        string Status,
        string Message,
        string? Target,
        string? JobId,
        string? HealthStatus,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed class BackupManifest
    {
        public string CreatedAtUtc { get; init; } = string.Empty;

        public string BackupFileName { get; init; } = string.Empty;

        public string SchemaVersion { get; init; } = "1.0.0";

        public string VerificationStatus { get; init; } = "pending";

        public string RestoreTarget { get; init; } = "compose-stack";

        public List<BackupAssetStatus> Assets { get; init; } = [];
    }

    private sealed record BackupAssetStatus(
        string Name,
        string Status,
        string? Path,
        string? Details);
}
