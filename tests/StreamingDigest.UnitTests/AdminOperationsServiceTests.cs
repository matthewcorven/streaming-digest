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
}
