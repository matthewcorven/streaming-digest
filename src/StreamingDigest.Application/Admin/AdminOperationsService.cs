using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Admin;

public sealed class AdminOperationsService : IAdminOperationsService
{
    private readonly ConcurrentDictionary<Guid, AdminActionStatus> _operations = new();
    private readonly ApplicationConfiguration _configuration;
    private readonly string? _contentRootPath;
    private readonly IAdminOperationStore? _operationStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ITranscriptIngestionService? _transcriptIngestionService;
    private readonly ISearchDocumentRegenerationService? _searchDocumentRegenerationService;
    private readonly IAudioToTextProvider? _audioToTextProvider;
    private readonly IModelReadinessGuard? _modelReadinessGuard;
    private readonly IIngestionJobScheduler? _ingestionJobScheduler;
    private readonly INotificationTestSender? _notificationTestSender;

    public AdminOperationsService(
        ApplicationConfiguration? configuration = null,
        string? contentRootPath = null,
        IAdminOperationStore? operationStore = null,
        IEmbeddingService? embeddingService = null,
        ITranscriptIngestionService? transcriptIngestionService = null,
        ISearchDocumentRegenerationService? searchDocumentRegenerationService = null,
        IAudioToTextProvider? audioToTextProvider = null,
        IModelReadinessGuard? modelReadinessGuard = null,
        IIngestionJobScheduler? ingestionJobScheduler = null,
        INotificationTestSender? notificationTestSender = null)
    {
        _configuration = configuration ?? new ApplicationConfiguration();
        _contentRootPath = contentRootPath;
        _operationStore = operationStore;
        _embeddingService = embeddingService ?? new NullEmbeddingService();
        _transcriptIngestionService = transcriptIngestionService;
        _searchDocumentRegenerationService = searchDocumentRegenerationService;
        _audioToTextProvider = audioToTextProvider;
        _modelReadinessGuard = modelReadinessGuard;
        _ingestionJobScheduler = ingestionJobScheduler;
        _notificationTestSender = notificationTestSender;
    }

    public async Task<AdminActionResult> RunIngestionNowAsync(string? target = null, CancellationToken cancellationToken = default)
    {
        var result = await CreateAcceptedResultAsync("ingestion.run", target, "Manual ingestion has been queued for the target scope.", cancellationToken);
        await TryPersistIngestionRunAsync(result.OperationId, "manual", target, cancellationToken);

        if (_ingestionJobScheduler is not null)
        {
            var channelId = TryParseChannelId(target);
            _ingestionJobScheduler.EnqueueOnDemandRun(channelId, "manual", "admin");
        }

        return result;
    }

    public async Task<AdminActionResult> RunChannelBackfillAsync(string? channelId = null, CancellationToken cancellationToken = default)
    {
        var result = await CreateAcceptedResultAsync("ingestion.backfill", channelId, "Channel backfill has been queued.", cancellationToken);
        await TryPersistIngestionRunAsync(result.OperationId, "backfill", channelId, cancellationToken);

        if (_ingestionJobScheduler is not null)
        {
            var parsedChannelId = TryParseChannelId(channelId);
            _ingestionJobScheduler.EnqueueOnDemandRun(parsedChannelId, "backfill", "admin");
        }

        return result;
    }

    public async Task<AdminActionResult> RetryFailedIngestionRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var parsedRunId))
        {
            return await CreateResultAsync("retry.ingestionRun", runId, "failed", $"Ingestion run id '{runId}' is not a valid GUID.", "error", cancellationToken);
        }

        var result = await CreateAcceptedResultAsync("retry.ingestionRun", runId, $"Retry queued for ingestion run '{runId}'.", cancellationToken);
        await TryRetryFailedRunAsync(result.OperationId, parsedRunId, cancellationToken);

        if (_ingestionJobScheduler is not null)
        {
            // Dispatch channel jobs so the pending items set above are actually processed.
            await TryDispatchRunRetryJobsAsync(parsedRunId, cancellationToken);
        }

        return result;
    }

    public async Task<AdminActionResult> RetryFailedVideoAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(videoId, out var parsedVideoId))
        {
            return await CreateResultAsync("retry.video", videoId, "failed", $"Video id '{videoId}' is not a valid GUID.", "error", cancellationToken);
        }

        if (_ingestionJobScheduler is not null)
        {
            // Mark failed/deferred items as pending (ADR-0002: retry re-executes failed/deferred work only),
            // then dispatch the orchestrator to pick them up.
            var result = await CreateAcceptedResultAsync("retry.video", videoId, $"Retry queued for video '{videoId}'.", cancellationToken);
            await TryRetryFailedEntityAsync(result.OperationId, "retry.video", "video", parsedVideoId, videoId, cancellationToken);
            await TryDispatchVideoChannelJobAsync(parsedVideoId, "manual", cancellationToken);
            return result;
        }

        return await RunTranscriptIngestionAsync(
            "retry.video",
            parsedVideoId,
            videoId,
            "Retry completed",
            cancellationToken);
    }

    public async Task<AdminActionResult> RetryFailedLinkAsync(string linkId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(linkId, out var parsedLinkId))
        {
            return await CreateResultAsync("retry.link", linkId, "failed", $"Link id '{linkId}' is not a valid GUID.", "error", cancellationToken);
        }

        var result = await CreateAcceptedResultAsync("retry.link", linkId, $"Retry queued for link '{linkId}'.", cancellationToken);
        await TryRetryFailedEntityAsync(result.OperationId, "retry.link", "link", parsedLinkId, linkId, cancellationToken);
        return result;
    }

    public async Task<AdminActionResult> RetryFailedRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(repositoryId, out var parsedRepositoryId))
        {
            return await CreateResultAsync("retry.repository", repositoryId, "failed", $"Repository id '{repositoryId}' is not a valid GUID.", "error", cancellationToken);
        }

        var result = await CreateAcceptedResultAsync("retry.repository", repositoryId, $"Retry queued for repository '{repositoryId}'.", cancellationToken);
        await TryRetryFailedEntityAsync(result.OperationId, "retry.repository", "repository", parsedRepositoryId, repositoryId, cancellationToken);
        return result;
    }

    public async Task<AdminActionResult> ReprocessVideoAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(videoId, out var parsedVideoId))
        {
            return await CreateResultAsync("reprocess.video", videoId, "failed", $"Video id '{videoId}' is not a valid GUID.", "error", cancellationToken);
        }

        if (_ingestionJobScheduler is not null)
        {
            // Reprocess bypasses the idempotency guard and resets budgets (ADR-0002/0014:
            // full pipeline for a completed entity, scrape-exclusion policy re-evaluated).
            var result = await CreateAcceptedResultAsync("reprocess.video", videoId, $"Reprocess queued for video '{videoId}'.", cancellationToken);
            await TryDispatchVideoChannelJobAsync(parsedVideoId, "reprocess", cancellationToken);
            return result;
        }

        return await RunTranscriptIngestionAsync(
            "reprocess.video",
            parsedVideoId,
            videoId,
            "Reprocess completed",
            cancellationToken);
    }

    public async Task<AdminActionResult> ReprocessRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(repositoryId, out var parsedRepositoryId))
        {
            return await CreateResultAsync("reprocess.repository", repositoryId, "failed", $"Repository id '{repositoryId}' is not a valid GUID.", "error", cancellationToken);
        }

        if (_searchDocumentRegenerationService is null)
        {
            return await CreateAcceptedResultAsync("reprocess.repository", repositoryId, $"Reprocess queued for repository '{repositoryId}'.", cancellationToken);
        }

        await _searchDocumentRegenerationService.RegenerateForRepositoryAsync(parsedRepositoryId, cancellationToken);
        return await CreateCompletedResultAsync("reprocess.repository", repositoryId, $"Reprocess completed for repository '{repositoryId}'.", "healthy", cancellationToken);
    }

    public async Task<AdminActionResult> ReprocessResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(resourceId, out var parsedResourceId))
        {
            return await CreateResultAsync("reprocess.resource", resourceId, "failed", $"Resource id '{resourceId}' is not a valid GUID.", "error", cancellationToken);
        }

        if (_searchDocumentRegenerationService is null)
        {
            return await CreateAcceptedResultAsync("reprocess.resource", resourceId, $"Reprocess queued for resource '{resourceId}'.", cancellationToken);
        }

        await _searchDocumentRegenerationService.RegenerateForExternalResourceAsync(parsedResourceId, cancellationToken);
        return await CreateCompletedResultAsync("reprocess.resource", resourceId, $"Reprocess completed for resource '{resourceId}'.", "healthy", cancellationToken);
    }

    public async Task<AdminActionResult> ReprocessEmbeddingsAsync(string? target = null, CancellationToken cancellationToken = default)
    {
        if (_searchDocumentRegenerationService is null)
        {
            return await CreateAcceptedResultAsync("reprocess.embeddings", target, "Embedding reprocessing has been queued for the requested scope.", cancellationToken);
        }

        await _searchDocumentRegenerationService.RegenerateAllAsync(cancellationToken);
        return await CreateCompletedResultAsync("reprocess.embeddings", target, "Embedding reprocessing completed successfully.", "healthy", cancellationToken);
    }

    public async Task<AdminActionResult> PurgeScreenshotsAsync(string? target = null, CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return await CreateAcceptedResultAsync("screenshots.purge", target, "Screenshot purge has been queued (no database connection configured).", cancellationToken);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var filePaths = new List<string>();
            var selectSql = string.IsNullOrWhiteSpace(target)
                ? "SELECT file_path FROM public.screenshots WHERE file_path IS NOT NULL"
                : "SELECT file_path FROM public.screenshots WHERE file_path IS NOT NULL AND (video_id::text = @target OR segment_id::text = @target)";

            await using (var selectCmd = new NpgsqlCommand(selectSql, connection, transaction))
            {
                if (!string.IsNullOrWhiteSpace(target))
                {
                    selectCmd.Parameters.AddWithValue("target", target.Trim());
                }
                await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        filePaths.Add(reader.GetString(0));
                    }
                }
            }

            var deleteSql = string.IsNullOrWhiteSpace(target)
                ? "DELETE FROM public.screenshots"
                : "DELETE FROM public.screenshots WHERE video_id::text = @target OR segment_id::text = @target";

            int deletedRows;
            await using (var deleteCmd = new NpgsqlCommand(deleteSql, connection, transaction))
            {
                if (!string.IsNullOrWhiteSpace(target))
                {
                    deleteCmd.Parameters.AddWithValue("target", target.Trim());
                }
                deletedRows = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            var deletedFiles = 0;
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        deletedFiles++;
                    }
                }
                catch
                {
                    // File deletion failures are best-effort; the metadata rows are already removed.
                }
            }

            var scopeDescription = string.IsNullOrWhiteSpace(target) ? "all targets" : $"target '{target}'";
            var message = $"Screenshot purge completed for {scopeDescription}. Deleted {deletedRows} metadata row(s) and {deletedFiles} file(s).";
            return await CreateCompletedResultAsync("screenshots.purge", target, message, "healthy", cancellationToken);
        }
        catch (Exception ex)
        {
            return await CreateResultAsync("screenshots.purge", target, "failed", $"Screenshot purge failed: {ex.Message}", "error", cancellationToken);
        }
    }

    public async Task<AdminActionResult> TestMatrixNotificationAsync(CancellationToken cancellationToken = default)
    {
        if (_notificationTestSender is null)
        {
            return await CreateCompletedResultAsync("test.matrix", null, "Matrix notification test skipped: no notification sender is registered.", "warning", cancellationToken);
        }

        if (!_notificationTestSender.IsEnabled)
        {
            return await CreateCompletedResultAsync("test.matrix", null, "Matrix notifications are disabled; test skipped.", "warning", cancellationToken);
        }

        try
        {
            var outcome = await _notificationTestSender.TestAsync(cancellationToken);
            return outcome.Success
                ? await CreateCompletedResultAsync("test.matrix", null, $"Matrix test notification sent successfully: {outcome.Message}", "healthy", cancellationToken)
                : await CreateResultAsync("test.matrix", null, "failed", $"Matrix test notification failed: {outcome.Message}", "error", cancellationToken);
        }
        catch (Exception ex)
        {
            return await CreateResultAsync("test.matrix", null, "failed", $"Matrix test notification threw an unexpected exception: {ex.Message}", "error", cancellationToken);
        }
    }

    public async Task<AdminActionResult> TestEmbeddingServiceAsync(CancellationToken cancellationToken = default)
    {
        // WS-7 S7: route through the shared model-readiness guard so the admin embedding test
        // reports a truthful unready reason instead of only a raw connection error.
        if (_modelReadinessGuard is not null)
        {
            var readiness = await _modelReadinessGuard.CheckAsync(RuntimeRole.Embedding, cancellationToken);
            if (!readiness.IsReady)
            {
                return await CreateResultAsync(
                    "test.embeddings",
                    null,
                    "failed",
                    $"Embedding service not ready: {readiness.Reason}",
                    "error",
                    cancellationToken);
            }
        }

        try
        {
            var sampleText = "The quick brown fox jumps over the lazy dog.";
            var embedding = await _embeddingService.GenerateEmbeddingAsync(sampleText, cancellationToken);
            var message = $"Embedding service health check completed successfully. Provider '{embedding.Provider}' model '{embedding.Model}' returned {embedding.Dimensions} dimensions for the sample text.";
            return await CreateCompletedResultAsync("test.embeddings", null, message, "healthy", cancellationToken);
        }
        catch (Exception ex)
        {
            return await CreateResultAsync("test.embeddings", null, "failed", $"Embedding service health check failed: {ex.Message}", "error", cancellationToken);
        }
    }

    public async Task<AdminActionResult> TestAudioToTextServiceAsync(CancellationToken cancellationToken = default)
    {
        // Truthful probe (issue #210): previously this returned a fake "completed/healthy"
        // without probing anything. It now delegates to the configured IAudioToTextProvider
        // (which performs a GET /health against the whisper service) and reports the real
        // status:
        //   - healthy provider                    -> completed / healthy
        //   - unavailable (but no throw)          -> completed (NOT failed) / warning, with reason
        //   - no provider registered (unconfigured) -> completed / warning (same as whisper-unconfigured:
        //        an optional runtime simply isn't wired — expected degrade, not a fault)
        //   - probe threw (genuine fault)         -> failed / error
        // Caption-less videos degrade to "unavailable_captions" + notify in TranscriptIngestionService.
        if (_audioToTextProvider is null)
        {
            const string noProviderMessage = "Audio-to-text service health check completed with warnings. No audio-to-text provider is registered (whisper runtime unconfigured). Caption-less videos will degrade to 'unavailable_captions' with a notify event; captioned ingestion proceeds with a warning.";
            return await CreateCompletedResultAsync("test.audio", null, noProviderMessage, "warning", cancellationToken);
        }

        try
        {
            var health = await _audioToTextProvider.CheckHealthAsync(cancellationToken);
            var endpoint = string.IsNullOrEmpty(health.Endpoint) ? "(unconfigured)" : health.Endpoint;
            var message = health.IsHealthy
                ? $"Audio-to-text service health check succeeded. Engine '{health.Engine}' at {endpoint} responded healthy: {health.Reason}"
                : $"Audio-to-text service health check completed with warnings. Engine '{health.Engine}' at {endpoint} is unavailable: {health.Reason} Caption-less videos will degrade to 'unavailable_captions' with a notify event; captioned ingestion proceeds with a warning.";

            var healthStatus = health.IsHealthy ? "healthy" : "warning";
            return await CreateCompletedResultAsync("test.audio", null, message, healthStatus, cancellationToken);
        }
        catch (Exception ex)
        {
            return await CreateResultAsync(
                "test.audio",
                null,
                "failed",
                $"Audio-to-text service health check failed: {ex.Message}",
                "error",
                cancellationToken);
        }
    }

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
                return await CreateCompletedResultAsync("backup.create", backupFileName, message, "healthy", cancellationToken);
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
            return await CreateResultAsync("backup.create", null, "failed", $"Backup creation failed: {ex.Message}", "error", cancellationToken);
        }
    }

    public async Task<AdminActionResult> RestoreLatestBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupDirectory = ResolveConfiguredDirectoryPath(_configuration.Backup.DestinationPath);
            if (!Directory.Exists(backupDirectory))
            {
                return await CreateResultAsync("backup.restore", null, "failed", $"Backup directory '{backupDirectory}' does not exist.", "error", cancellationToken);
            }

            var archivePath = Directory.EnumerateFiles(backupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return await CreateResultAsync("backup.restore", null, "failed", $"No backup archives were found in '{backupDirectory}'.", "error", cancellationToken);
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
            return await CreateCompletedResultAsync("backup.restore", archiveFileName, message, "healthy", cancellationToken);
        }
        catch (Exception ex)
        {
            return await CreateResultAsync("backup.restore", null, "failed", $"Backup restore failed: {ex.Message}", "error", cancellationToken);
        }
    }

    public async Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            return operation;
        }

        if (_operationStore is not null)
        {
            try
            {
                var persistedOperation = await _operationStore.GetOperationAsync(operationId, cancellationToken);
                if (persistedOperation is not null)
                {
                    _operations[operationId] = persistedOperation;
                    return persistedOperation;
                }
            }
            catch
            {
                // Keep memory-backed lookups available even when the persistence store is unavailable.
            }
        }

        return null;
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

    private sealed class NullEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(new EmbeddingGenerationResult("null", "null", 1, new[] { 0.0 }));
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

    private async Task TryDispatchVideoChannelJobAsync(
        Guid videoId,
        string runType,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(
                "SELECT channel_id FROM public.videos WHERE id = @id",
                connection);
            cmd.Parameters.AddWithValue("id", videoId);
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);

            if (scalar is not Guid channelId)
            {
                return;
            }

            _ingestionJobScheduler!.EnqueueOnDemandRun(channelId, runType, "admin");
        }
        catch
        {
            // Keep operation responses available even when dispatch fails.
        }
    }

    private async Task TryDispatchRunRetryJobsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Resolve distinct channels associated with this run via videos in the items.
            var channelIds = new HashSet<Guid>();
            await using (var cmd = new NpgsqlCommand("""
                SELECT DISTINCT v.channel_id
                FROM public.ingestion_items ii
                JOIN public.videos v ON v.id = ii.item_id
                WHERE ii.ingestion_run_id = @run_id
                  AND ii.item_type = 'video'
                """, connection))
            {
                cmd.Parameters.AddWithValue("run_id", runId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        channelIds.Add(reader.GetGuid(0));
                    }
                }
            }

            foreach (var channelId in channelIds)
            {
                _ingestionJobScheduler!.EnqueueOnDemandRun(channelId, "manual", "admin");
            }
        }
        catch
        {
            // Keep operation responses available even when dispatch fails.
        }
    }

    private async Task TryPersistIngestionRunAsync(Guid operationId, string runType, string? target, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var runId = Guid.NewGuid();
            var channels = await LoadRunChannelsAsync(connection, transaction, target, cancellationToken);
            await PersistOperationAsync(CreateOperationStatus(operationId, $"ingestion.{runType}", "accepted", "Manual ingestion is queued for the selected scope.", target, null, null, now, now), cancellationToken);

            await using (var operationCommand = new NpgsqlCommand("""
                INSERT INTO public.operations (
                    id, operation_type, status, requested_by, related_entity_type, started_at, created_at, updated_at
                )
                VALUES (
                    @id, @operation_type, @status, @requested_by, @related_entity_type, @started_at, @created_at, @updated_at
                )
                ON CONFLICT (id) DO UPDATE
                SET status = EXCLUDED.status,
                    updated_at = EXCLUDED.updated_at
                """, connection, transaction))
            {
                operationCommand.Parameters.AddWithValue("id", operationId);
                operationCommand.Parameters.AddWithValue("operation_type", $"ingestion.{runType}");
                operationCommand.Parameters.AddWithValue("status", "accepted");
                operationCommand.Parameters.AddWithValue("requested_by", "admin");
                operationCommand.Parameters.AddWithValue("related_entity_type", "ingestion_run");
                operationCommand.Parameters.AddWithValue("started_at", now);
                operationCommand.Parameters.AddWithValue("created_at", now);
                operationCommand.Parameters.AddWithValue("updated_at", now);
                await operationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var summary = new JsonObject
            {
                ["seededItemCount"] = channels.Count,
                ["seededAtUtc"] = now
            };

            await using (var runCommand = new NpgsqlCommand("""
                INSERT INTO public.ingestion_runs (
                    id, operation_id, run_type, triggered_by, status, started_at, channels_checked,
                    new_videos_found, videos_ingested, videos_failed, videos_skipped,
                    transcripts_found, transcripts_missing, repositories_found, summary_json, created_at
                )
                VALUES (
                    @id, @operation_id, @run_type, @triggered_by, @status, @started_at, @channels_checked,
                    0, 0, 0, 0, 0, 0, 0, @summary_json::jsonb, @created_at
                )
                """, connection, transaction))
            {
                runCommand.Parameters.AddWithValue("id", runId);
                runCommand.Parameters.AddWithValue("operation_id", operationId);
                runCommand.Parameters.AddWithValue("run_type", runType);
                runCommand.Parameters.AddWithValue("triggered_by", "admin");
                runCommand.Parameters.AddWithValue("status", "in_progress");
                runCommand.Parameters.AddWithValue("started_at", now);
                runCommand.Parameters.AddWithValue("channels_checked", channels.Count);
                runCommand.Parameters.AddWithValue("summary_json", summary.ToJsonString());
                runCommand.Parameters.AddWithValue("created_at", now);
                await runCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var channel in channels)
            {
                await using var itemCommand = new NpgsqlCommand("""
                    INSERT INTO public.ingestion_items (
                        id, ingestion_run_id, operation_id, item_type, item_id, external_key, idempotency_key,
                        stage, status, attempt, retry_count, max_attempts, is_retryable, created_at
                    )
                    VALUES (
                        @id, @ingestion_run_id, @operation_id, @item_type, @item_id, @external_key, @idempotency_key,
                        @stage, @status, 0, 0, 7, true, @created_at
                    )
                    """, connection, transaction);
                itemCommand.Parameters.AddWithValue("id", Guid.NewGuid());
                itemCommand.Parameters.AddWithValue("ingestion_run_id", runId);
                itemCommand.Parameters.AddWithValue("operation_id", operationId);
                itemCommand.Parameters.AddWithValue("item_type", "channel");
                itemCommand.Parameters.AddWithValue("item_id", channel.ChannelId);
                itemCommand.Parameters.AddWithValue("external_key", channel.ExternalKey);
                itemCommand.Parameters.AddWithValue("idempotency_key", $"ingestion:{runId:N}:{channel.ChannelId:N}");
                itemCommand.Parameters.AddWithValue("stage", "metadata");
                itemCommand.Parameters.AddWithValue("status", "pending");
                itemCommand.Parameters.AddWithValue("created_at", now);
                await itemCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Keep admin action responses available even when persistence is unavailable.
        }
    }

    private async Task TryRetryFailedRunAsync(Guid operationId, Guid runId, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            await PersistOperationAsync(CreateOperationStatus(operationId, "retry.ingestionRun", "accepted", $"Retry queued for ingestion run '{runId}'.", runId.ToString(), null, null, now, now), cancellationToken);

            await using (var operationCommand = new NpgsqlCommand("""
                INSERT INTO public.operations (
                    id, operation_type, status, requested_by, related_entity_type, related_entity_id, started_at, summary_json, created_at, updated_at
                )
                VALUES (
                    @id, @operation_type, @status, @requested_by, @related_entity_type, @related_entity_id, @started_at, @summary_json::jsonb, @created_at, @updated_at
                )
                ON CONFLICT (id) DO UPDATE
                SET status = EXCLUDED.status,
                    summary_json = EXCLUDED.summary_json,
                    updated_at = EXCLUDED.updated_at
                """, connection, transaction))
            {
                operationCommand.Parameters.AddWithValue("id", operationId);
                operationCommand.Parameters.AddWithValue("operation_type", "retry.ingestionRun");
                operationCommand.Parameters.AddWithValue("status", "accepted");
                operationCommand.Parameters.AddWithValue("requested_by", "admin");
                operationCommand.Parameters.AddWithValue("related_entity_type", "ingestion_run");
                operationCommand.Parameters.AddWithValue("related_entity_id", runId);
                operationCommand.Parameters.AddWithValue("started_at", now);
                operationCommand.Parameters.AddWithValue("summary_json", new JsonObject
                {
                    ["retryScope"] = "ingestion_run",
                    ["ingestionRunId"] = runId,
                    ["queuedAtUtc"] = now
                }.ToJsonString());
                operationCommand.Parameters.AddWithValue("created_at", now);
                operationCommand.Parameters.AddWithValue("updated_at", now);
                await operationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            List<RetryQueueRow> queuedRetryRows = [];
            await using (var retryItemsCommand = new NpgsqlCommand("""
                UPDATE public.ingestion_items
                SET operation_id = @operation_id,
                    status = 'pending',
                    attempt = attempt + 1,
                    retry_count = retry_count + 1,
                    error_summary = NULL,
                    next_retry_at = NULL,
                    deferred_until = NULL,
                    deferment_reason = NULL
                WHERE ingestion_run_id = @ingestion_run_id
                    AND is_retryable = true
                    AND status IN ('failed', 'deferred')
                RETURNING id, ingestion_run_id, item_type, item_id, stage, attempt, retry_count
                """, connection, transaction))
            {
                retryItemsCommand.Parameters.AddWithValue("operation_id", operationId);
                retryItemsCommand.Parameters.AddWithValue("ingestion_run_id", runId);
                await using var reader = await retryItemsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    queuedRetryRows.Add(new RetryQueueRow(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetGuid(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6)));
                }
            }

            foreach (var retryRow in queuedRetryRows)
            {
                await InsertRetryDomainEventAsync(connection, transaction, operationId, retryRow, "ingestion_run", runId.ToString(), now, cancellationToken);
            }

            await using (var runCommand = new NpgsqlCommand("""
                UPDATE public.ingestion_runs
                SET operation_id = @operation_id,
                    status = 'in_progress'
                WHERE id = @id
                """, connection, transaction))
            {
                runCommand.Parameters.AddWithValue("operation_id", operationId);
                runCommand.Parameters.AddWithValue("id", runId);
                await runCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateSummaryCommand = new NpgsqlCommand("""
                UPDATE public.operations
                SET summary_json = @summary_json::jsonb,
                    updated_at = @updated_at
                WHERE id = @id
                """, connection, transaction))
            {
                updateSummaryCommand.Parameters.AddWithValue("id", operationId);
                updateSummaryCommand.Parameters.AddWithValue("summary_json", new JsonObject
                {
                    ["retryScope"] = "ingestion_run",
                    ["ingestionRunId"] = runId,
                    ["queuedItemCount"] = queuedRetryRows.Count,
                    ["queuedAtUtc"] = now
                }.ToJsonString());
                updateSummaryCommand.Parameters.AddWithValue("updated_at", now);
                await updateSummaryCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Keep retry operation accepted responses available even when persistence is unavailable.
        }
    }

    private async Task TryRetryFailedEntityAsync(
        Guid operationId,
        string operationType,
        string itemType,
        Guid itemId,
        string itemIdText,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration.ConnectionStrings.StreamingDigest;
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("******", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            await PersistOperationAsync(CreateOperationStatus(operationId, operationType, "accepted", $"Retry queued for {itemType} '{itemIdText}'.", itemIdText, null, null, now, now), cancellationToken);

            await using (var operationCommand = new NpgsqlCommand("""
                INSERT INTO public.operations (
                    id, operation_type, status, requested_by, related_entity_type, related_entity_id, started_at, summary_json, created_at, updated_at
                )
                VALUES (
                    @id, @operation_type, @status, @requested_by, @related_entity_type, @related_entity_id, @started_at, @summary_json::jsonb, @created_at, @updated_at
                )
                ON CONFLICT (id) DO UPDATE
                SET status = EXCLUDED.status,
                    summary_json = EXCLUDED.summary_json,
                    updated_at = EXCLUDED.updated_at
                """, connection, transaction))
            {
                operationCommand.Parameters.AddWithValue("id", operationId);
                operationCommand.Parameters.AddWithValue("operation_type", operationType);
                operationCommand.Parameters.AddWithValue("status", "accepted");
                operationCommand.Parameters.AddWithValue("requested_by", "admin");
                operationCommand.Parameters.AddWithValue("related_entity_type", itemType);
                operationCommand.Parameters.AddWithValue("related_entity_id", itemId);
                operationCommand.Parameters.AddWithValue("started_at", now);
                operationCommand.Parameters.AddWithValue("summary_json", new JsonObject
                {
                    ["retryScope"] = itemType,
                    ["requestedEntityId"] = itemId,
                    ["queuedAtUtc"] = now
                }.ToJsonString());
                operationCommand.Parameters.AddWithValue("created_at", now);
                operationCommand.Parameters.AddWithValue("updated_at", now);
                await operationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            List<RetryQueueRow> queuedRetryRows = [];
            await using (var retryItemsCommand = new NpgsqlCommand("""
                UPDATE public.ingestion_items
                SET operation_id = @operation_id,
                    status = 'pending',
                    attempt = attempt + 1,
                    retry_count = retry_count + 1,
                    error_summary = NULL,
                    next_retry_at = NULL,
                    deferred_until = NULL,
                    deferment_reason = NULL
                WHERE item_type = @item_type
                    AND item_id = @item_id
                    AND is_retryable = true
                    AND status IN ('failed', 'deferred')
                RETURNING id, ingestion_run_id, item_type, item_id, stage, attempt, retry_count
                """, connection, transaction))
            {
                retryItemsCommand.Parameters.AddWithValue("operation_id", operationId);
                retryItemsCommand.Parameters.AddWithValue("item_type", itemType);
                retryItemsCommand.Parameters.AddWithValue("item_id", itemId);
                await using var reader = await retryItemsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    queuedRetryRows.Add(new RetryQueueRow(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetGuid(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6)));
                }
            }

            foreach (var retryRow in queuedRetryRows)
            {
                await InsertRetryDomainEventAsync(connection, transaction, operationId, retryRow, itemType, itemIdText, now, cancellationToken);
            }

            await using (var updateSummaryCommand = new NpgsqlCommand("""
                UPDATE public.operations
                SET summary_json = @summary_json::jsonb,
                    updated_at = @updated_at
                WHERE id = @id
                """, connection, transaction))
            {
                updateSummaryCommand.Parameters.AddWithValue("id", operationId);
                updateSummaryCommand.Parameters.AddWithValue("summary_json", new JsonObject
                {
                    ["retryScope"] = itemType,
                    ["requestedEntityId"] = itemId,
                    ["queuedItemCount"] = queuedRetryRows.Count,
                    ["queuedAtUtc"] = now
                }.ToJsonString());
                updateSummaryCommand.Parameters.AddWithValue("updated_at", now);
                await updateSummaryCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Keep retry operation accepted responses available even when persistence is unavailable.
        }
    }

    private static async Task InsertRetryDomainEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        RetryQueueRow retryRow,
        string retryScope,
        string scopeTarget,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var details = new JsonObject
        {
            ["retryScope"] = retryScope,
            ["scopeTarget"] = scopeTarget,
            ["itemType"] = retryRow.ItemType,
            ["itemId"] = retryRow.ItemId,
            ["stage"] = retryRow.Stage,
            ["attempt"] = retryRow.Attempt,
            ["retryCount"] = retryRow.RetryCount
        };

        await using var eventCommand = new NpgsqlCommand("""
            INSERT INTO public.domain_events (
                id, event_type, severity, entity_type, entity_id, ingestion_run_id, operation_id, message, details_json, created_at, updated_at
            )
            VALUES (
                @id, @event_type, @severity, @entity_type, @entity_id, @ingestion_run_id, @operation_id, @message, @details_json::jsonb, @created_at, @updated_at
            )
            """, connection, transaction);
        eventCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        eventCommand.Parameters.AddWithValue("event_type", "ingestion.item.retry_queued");
        eventCommand.Parameters.AddWithValue("severity", "info");
        eventCommand.Parameters.AddWithValue("entity_type", "ingestion_item");
        eventCommand.Parameters.AddWithValue("entity_id", retryRow.IngestionItemId);
        eventCommand.Parameters.AddWithValue("ingestion_run_id", retryRow.IngestionRunId);
        eventCommand.Parameters.AddWithValue("operation_id", operationId);
        eventCommand.Parameters.AddWithValue("message", $"Retry queued for {retryRow.ItemType} stage '{retryRow.Stage}'.");
        eventCommand.Parameters.AddWithValue("details_json", details.ToJsonString());
        eventCommand.Parameters.AddWithValue("created_at", timestamp);
        eventCommand.Parameters.AddWithValue("updated_at", timestamp);
        await eventCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<(Guid ChannelId, string ExternalKey)>> LoadRunChannelsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string? target,
        CancellationToken cancellationToken)
    {
        var channels = new List<(Guid ChannelId, string ExternalKey)>();
        var commandText = """
            SELECT id, youtube_channel_id
            FROM public.channels
            WHERE is_paused = false
            """;

        var hasTarget = !string.IsNullOrWhiteSpace(target);
        if (hasTarget)
        {
            commandText += " AND (id::text = @target OR youtube_channel_id = @target)";
        }

        await using var command = new NpgsqlCommand(commandText, connection, transaction);
        if (hasTarget)
        {
            command.Parameters.AddWithValue("target", target!.Trim());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            channels.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        return channels;
    }

    private async Task<AdminActionResult> CreateAcceptedResultAsync(string operationType, string? target, string message, CancellationToken cancellationToken)
    {
        var operation = CreateOperationStatus(Guid.NewGuid(), operationType, "accepted", message, target, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _operations[operation.OperationId] = operation;
        await PersistOperationAsync(operation, cancellationToken);
        return new AdminActionResult(operation.OperationId, operation.OperationType, operation.Status, operation.Message, operation.Target, operation.JobId, operation.HealthStatus);
    }

    private async Task<AdminActionResult> RunTranscriptIngestionAsync(
        string operationType,
        Guid videoId,
        string target,
        string successPrefix,
        CancellationToken cancellationToken)
    {
        if (_transcriptIngestionService is null)
        {
            return await CreateResultAsync(
                operationType,
                target,
                "failed",
                "Transcript ingestion service is not configured for video operations.",
                "error",
                cancellationToken);
        }

        var ingestionResult = await _transcriptIngestionService.IngestAsync(videoId, cancellationToken);
        if (!ingestionResult.Succeeded)
        {
            var failureMessage = ingestionResult.ErrorMessage ?? "transcript_ingestion_failed";
            return await CreateResultAsync(
                operationType,
                target,
                "failed",
                $"Transcript ingestion failed for video '{target}': {failureMessage}.",
                "error",
                cancellationToken);
        }

        if (ingestionResult.Skipped)
        {
            return await CreateCompletedResultAsync(
                operationType,
                target,
                $"{successPrefix} for video '{target}' was skipped by transcript ingestion.",
                "healthy",
                cancellationToken);
        }

        var transcriptId = ingestionResult.TranscriptId?.ToString() ?? "unknown";
        var sourceType = ingestionResult.SourceType ?? "unknown";
        return await CreateCompletedResultAsync(
            operationType,
            target,
            $"{successPrefix} for video '{target}'. Transcript '{transcriptId}' stored from '{sourceType}' with {ingestionResult.CueCount} cue(s).",
            "healthy",
            cancellationToken);
    }

    private async Task<AdminActionResult> CreateCompletedResultAsync(string operationType, string? target, string message, string healthStatus, CancellationToken cancellationToken)
        => await CreateResultAsync(operationType, target, "completed", message, healthStatus, cancellationToken);

    private async Task<AdminActionResult> CreateResultAsync(string operationType, string? target, string status, string message, string? healthStatus, CancellationToken cancellationToken)
    {
        var operation = CreateOperationStatus(Guid.NewGuid(), operationType, status, message, target, null, healthStatus, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _operations[operation.OperationId] = operation;
        await PersistOperationAsync(operation, cancellationToken);
        return new AdminActionResult(operation.OperationId, operation.OperationType, operation.Status, operation.Message, operation.Target, operation.JobId, operation.HealthStatus);
    }

    private static AdminActionStatus CreateOperationStatus(Guid operationId, string operationType, string status, string message, string? target, string? jobId, string? healthStatus, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(operationId, operationType, status, message, target, jobId, healthStatus, createdAt, updatedAt);

    private async Task PersistOperationAsync(AdminActionStatus operation, CancellationToken cancellationToken)
    {
        if (_operationStore is null)
        {
            return;
        }

        try
        {
            await _operationStore.PersistOperationAsync(operation, cancellationToken);
        }
        catch
        {
            // Keep the in-memory operation tracking available even when persistence fails.
        }
    }

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

    private sealed record RetryQueueRow(
        Guid IngestionItemId,
        Guid IngestionRunId,
        string ItemType,
        Guid? ItemId,
        string Stage,
        int Attempt,
        int RetryCount);

    private static Guid? TryParseChannelId(string? value)
        => Guid.TryParse(value, out var id) ? id : null;
}
