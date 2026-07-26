using System.IO.Compression;
using StreamingDigest.Application.Admin;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.UnitTests;

public sealed class AdminOperationsServiceTests
{
    [Fact]
    public async Task RunIngestionNowAsync_ReturnsAcceptedOperation()
    {
        var service = new AdminOperationsService();

        var result = await service.RunIngestionNowAsync("channel-1");

        Assert.Equal("accepted", result.Status);
        Assert.Equal("ingestion.run", result.OperationType);
        Assert.Equal("channel-1", result.Target);
        Assert.NotEqual(Guid.Empty, result.OperationId);

        var operation = await service.GetOperationAsync(result.OperationId);
        Assert.NotNull(operation);
        Assert.Equal(result.OperationType, operation!.OperationType);
    }

    [Fact]
    public async Task TestEmbeddingServiceAsync_ReturnsCompletedHealthResult()
    {
        var service = new AdminOperationsService();

        var result = await service.TestEmbeddingServiceAsync();

        Assert.Equal("completed", result.Status);
        Assert.Equal("test.embeddings", result.OperationType);
        Assert.Equal("healthy", result.HealthStatus);

        var operation = await service.GetOperationAsync(result.OperationId);
        Assert.NotNull(operation);
        Assert.Equal("healthy", operation!.HealthStatus);
    }

    [Fact]
    public async Task CreateBackupAsync_CreatesArchiveAndManifest()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"streaming-digest-backup-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(Path.Combine(tempDirectory, "media"));
            Directory.CreateDirectory(Path.Combine(tempDirectory, "matrix"));
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, "appsettings.json"), "{\"configSchemaVersion\":\"1.0.0\"}");
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, "appsettings.schema.json"), "{}" );
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, ".env"), "STREAMING_DIGEST_TEST=1\n");
            await File.WriteAllTextAsync(Path.Combine(tempDirectory, "media", "screenshot.png"), "placeholder");

            var configuration = new ApplicationConfiguration
            {
                Backup = new BackupSettings
                {
                    DestinationPath = Path.Combine(tempDirectory, "backups"),
                    MediaPath = Path.Combine(tempDirectory, "media"),
                    MatrixPath = Path.Combine(tempDirectory, "matrix"),
                    IncludeAppSettings = true,
                    IncludeSecrets = true
                },
                ConnectionStrings = new ConnectionStringsSettings
                {
                    StreamingDigest = "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;Password=postgres"
                }
            };

            var service = new AdminOperationsService(configuration, tempDirectory);
            var result = await service.CreateBackupAsync();

            Assert.Equal("completed", result.Status);
            Assert.Equal("backup.create", result.OperationType);
            Assert.NotNull(result.Target);

            var archivePath = Path.Combine(tempDirectory, "backups", result.Target!);
            Assert.True(File.Exists(archivePath));

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "config/appsettings.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RestoreLatestBackupAsync_WithPostgresDumpEntry_RestoresDatabaseViaPsql()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"streaming-digest-backup-restore-tests-{Guid.NewGuid():N}");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(Path.Combine(tempDirectory, "media"));
            Directory.CreateDirectory(Path.Combine(tempDirectory, "matrix"));
            Directory.CreateDirectory(Path.Combine(tempDirectory, "backups"));

            var shimDirectory = Path.Combine(tempDirectory, "shims");
            Directory.CreateDirectory(shimDirectory);
            var logPath = Path.Combine(tempDirectory, "psql.log");

            var psqlPath = Path.Combine(shimDirectory, OperatingSystem.IsWindows() ? "psql.cmd" : "psql");
            var shimScript = OperatingSystem.IsWindows()
                ? "@echo off\r\nsetlocal\r\n> \"%PSQL_LOG_PATH%\" echo %*\r\n"
                : "#!/bin/sh\nset -eu\nprintf '%s\n' \"$@\" > \"$PSQL_LOG_PATH\"\n";
            await File.WriteAllTextAsync(psqlPath, shimScript);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(psqlPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Environment.SetEnvironmentVariable("PATH", $"{shimDirectory}{Path.PathSeparator}{originalPath}");
            Environment.SetEnvironmentVariable("PSQL_LOG_PATH", logPath);

            var configuration = new ApplicationConfiguration
            {
                Backup = new BackupSettings
                {
                    DestinationPath = Path.Combine(tempDirectory, "backups"),
                    MediaPath = Path.Combine(tempDirectory, "media"),
                    MatrixPath = Path.Combine(tempDirectory, "matrix"),
                    IncludeAppSettings = true,
                    IncludeSecrets = true
                },
                ConnectionStrings = new ConnectionStringsSettings
                {
                    StreamingDigest = "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;Password=postgres"
                }
            };

            var backupArchivePath = Path.Combine(tempDirectory, "backups", "streaming-digest-backup-20260726-150000.zip");
            await using (var archiveStream = File.Create(backupArchivePath))
            {
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);

                var postgresEntry = archive.CreateEntry("postgresql/postgres.sql");
                await using (var postgresEntryStream = postgresEntry.Open())
                {
                    await using var postgresWriter = new StreamWriter(postgresEntryStream);
                    await postgresWriter.WriteAsync("CREATE TABLE test(id integer);");
                }
            }

            var service = new AdminOperationsService(configuration, tempDirectory);
            var result = await service.RestoreLatestBackupAsync();

            Assert.Equal("completed", result.Status);
            Assert.Equal("backup.restore", result.OperationType);
            Assert.Contains("PostgreSQL database restored", result.Message, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(logPath));
            var logContents = await File.ReadAllTextAsync(logPath);
            Assert.Contains("--single-transaction", logContents, StringComparison.Ordinal);
            Assert.Contains("--set", logContents, StringComparison.Ordinal);
            Assert.Contains("ON_ERROR_STOP=1", logContents, StringComparison.Ordinal);
            Assert.Contains("--dbname", logContents, StringComparison.Ordinal);
            Assert.Contains("streamingdigest", logContents, StringComparison.Ordinal);
            Assert.Contains("--file", logContents, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PSQL_LOG_PATH", null);
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RestoreLatestBackupAsync_RestoresArchiveContentsIntoConfiguredLocations()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"streaming-digest-backup-restore-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(Path.Combine(tempDirectory, "media"));
            Directory.CreateDirectory(Path.Combine(tempDirectory, "matrix"));
            Directory.CreateDirectory(Path.Combine(tempDirectory, "backups"));

            var configuration = new ApplicationConfiguration
            {
                Backup = new BackupSettings
                {
                    DestinationPath = Path.Combine(tempDirectory, "backups"),
                    MediaPath = Path.Combine(tempDirectory, "media"),
                    MatrixPath = Path.Combine(tempDirectory, "matrix"),
                    IncludeAppSettings = true,
                    IncludeSecrets = true
                }
            };

            var backupArchivePath = Path.Combine(tempDirectory, "backups", "streaming-digest-backup-20260726-150000.zip");
            await using (var archiveStream = File.Create(backupArchivePath))
            {
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);

                var mediaEntry = archive.CreateEntry("media/screenshot.png");
                await using (var mediaEntryStream = mediaEntry.Open())
                {
                    await using var mediaWriter = new StreamWriter(mediaEntryStream);
                    await mediaWriter.WriteAsync("restored media");
                }

                var matrixEntry = archive.CreateEntry("matrix/room.json");
                await using (var matrixEntryStream = matrixEntry.Open())
                {
                    await using var matrixWriter = new StreamWriter(matrixEntryStream);
                    await matrixWriter.WriteAsync("restored matrix");
                }

                var configEntry = archive.CreateEntry("config/appsettings.json");
                await using (var configEntryStream = configEntry.Open())
                {
                    await using var configWriter = new StreamWriter(configEntryStream);
                    await configWriter.WriteAsync("{\"configSchemaVersion\":\"2.0.0\"}");
                }
            }

            var service = new AdminOperationsService(configuration, tempDirectory);
            var result = await service.RestoreLatestBackupAsync();

            Assert.Equal("completed", result.Status);
            Assert.Equal("backup.restore", result.OperationType);

            var restoredMediaPath = Path.Combine(tempDirectory, "media", "screenshot.png");
            var restoredMatrixPath = Path.Combine(tempDirectory, "matrix", "room.json");
            var restoredConfigPath = Path.Combine(tempDirectory, "appsettings.json");

            Assert.True(File.Exists(restoredMediaPath));
            Assert.True(File.Exists(restoredMatrixPath));
            Assert.True(File.Exists(restoredConfigPath));
            Assert.Equal("restored media", await File.ReadAllTextAsync(restoredMediaPath));
            Assert.Equal("restored matrix", await File.ReadAllTextAsync(restoredMatrixPath));
            Assert.Equal("{\"configSchemaVersion\":\"2.0.0\"}", await File.ReadAllTextAsync(restoredConfigPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
