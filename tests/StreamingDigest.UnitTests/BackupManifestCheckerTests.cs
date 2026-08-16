using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Services.Health;
using StreamingDigest.Infrastructure.Services.Health;

namespace StreamingDigest.UnitTests;

public class BackupManifestCheckerTests
{
    private readonly ILogger<BackupManifestChecker> _logger = NullLogger<BackupManifestChecker>.Instance;

    private static IHostEnvironment CreateMockHostEnvironment(string contentRootPath)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.ContentRootPath).Returns(contentRootPath);
        mock.Setup(e => e.EnvironmentName).Returns("Test");
        return mock.Object;
    }

    [Fact]
    public async Task GetBackupReadinessAsync_WithNoBackupDirectory_ReturnsUnhealthyStatus()
    {
        var config = new ApplicationConfiguration
        {
            Backup = new BackupSettings { DestinationPath = "/nonexistent/backup/path" }
        };
        var hostEnv = CreateMockHostEnvironment(Path.GetTempPath());
        var checker = new BackupManifestChecker(config, hostEnv, _logger);

        var result = await checker.GetBackupReadinessAsync();

        Assert.False(result.IsHealthy);
        Assert.True(result.IsError);
        Assert.Null(result.LastBackupAtUtc);
        Assert.Equal("No backup directory", result.Status);
        Assert.Contains("not found", result.Details.First());
    }

    [Fact]
    public async Task GetBackupReadinessAsync_WithEmptyBackupDirectory_ReturnsUnhealthyStatus()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ApplicationConfiguration
            {
                Backup = new BackupSettings { DestinationPath = tempDir }
            };
            var hostEnv = CreateMockHostEnvironment(Path.GetTempPath());
            var checker = new BackupManifestChecker(config, hostEnv, _logger);

            var result = await checker.GetBackupReadinessAsync();

            Assert.False(result.IsHealthy);
            Assert.False(result.IsError);
            Assert.Null(result.LastBackupAtUtc);
            Assert.Equal("No backups available", result.Status);
            Assert.Contains("No backup archives", result.Details.First());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetBackupReadinessAsync_WithValidBackup_ReturnsHealthyStatus()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a valid backup archive with manifest
            var backupFileName = "streaming-digest-backup-20260815-120000.zip";
            var backupPath = Path.Combine(tempDir, backupFileName);
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                var now = DateTime.UtcNow;
                var manifest = new
                {
                    createdAtUtc = now.ToString("o"),
                    backupFileName = backupFileName,
                    schemaVersion = "1.0.0",
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[]
                    {
                        new { name = "postgres", status = "completed", path = "/backup/postgres.sql" },
                        new { name = "media", status = "completed", path = "/backup/media.tar.gz" }
                    }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings
                    {
                        DestinationPath = tempDir,
                        MaxAgeHours = 72,
                        MinimumBackupCount = 1
                    }
                };
                var hostEnv = CreateMockHostEnvironment(Path.GetTempPath());
                var checker = new BackupManifestChecker(config, hostEnv, _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.True(result.IsHealthy);
                Assert.False(result.IsError);
                Assert.NotNull(result.LastBackupAtUtc);
                Assert.Contains("verified", result.Status.ToLower());
                Assert.NotEmpty(result.Details);
                Assert.StartsWith("Backup: ", result.Details.First());
                Assert.NotEqual("Never", result.TimeSinceLastBackup);
            }
            finally
            {
                Directory.Delete(stagingDir, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetBackupReadinessAsync_WithPendingBackup_ReturnsUnhealthyButVerifiable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var backupFileName = "streaming-digest-backup-20260815-120000.zip";
            var backupPath = Path.Combine(tempDir, backupFileName);
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                var now = DateTime.UtcNow;
                var manifest = new
                {
                    createdAtUtc = now.ToString("o"),
                    backupFileName = backupFileName,
                    schemaVersion = "1.0.0",
                    verificationStatus = "pending",
                    restoreTarget = "compose-stack",
                    assets = new[]
                    {
                        new { name = "postgres", status = "pending", path = "/backup/postgres.sql" }
                    }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings
                    {
                        DestinationPath = tempDir,
                        MaxAgeHours = 72,
                        MinimumBackupCount = 1
                    }
                };
                var hostEnv = CreateMockHostEnvironment(Path.GetTempPath());
                var checker = new BackupManifestChecker(config, hostEnv, _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.False(result.IsHealthy);
                Assert.False(result.IsError);
                Assert.NotNull(result.LastBackupAtUtc);
                Assert.Contains("pending", result.Status);
            }
            finally
            {
                Directory.Delete(stagingDir, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetBackupReadinessAsync_WithNoManifest_ReturnsUnhealthyStatus()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var backupFileName = "streaming-digest-backup-20260815-120000.zip";
            var backupPath = Path.Combine(tempDir, backupFileName);
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                // Create zip without manifest
                var dummyFile = Path.Combine(stagingDir, "dummy.txt");
                await File.WriteAllTextAsync(dummyFile, "test");

                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings
                    {
                        DestinationPath = tempDir,
                        MaxAgeHours = 72,
                        MinimumBackupCount = 1
                    }
                };
                var hostEnv = CreateMockHostEnvironment(Path.GetTempPath());
                var checker = new BackupManifestChecker(config, hostEnv, _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.False(result.IsHealthy);
                Assert.False(result.IsError);
                Assert.NotNull(result.LastBackupAtUtc);
                Assert.Contains("unreadable", result.Status);
            }
            finally
            {
                Directory.Delete(stagingDir, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("2026-08-15T12:00:00.000Z", "Just now")] // Very recent
    [InlineData(null, "Never")] // No backup
    public void BackupReadinessData_TimeSinceLastBackup_ReturnsCorrectFormat(string? createdAtUtc, string expectedPrefix)
    {
        var lastBackupAt = createdAtUtc switch
        {
            null => null,
            _ when DateTime.TryParse(createdAtUtc, out var dt) => dt,
            _ => (DateTime?)null
        };

        var data = new BackupReadinessData(
            IsHealthy: true,
            IsError: false,
            LastBackupAtUtc: lastBackupAt,
            Status: "Verified",
            Details: []);

        if (expectedPrefix == "Never")
        {
            Assert.Equal("Never", data.TimeSinceLastBackup);
        }
        else if (expectedPrefix == "Just now")
        {
            // Should end with "ago" or be "Just now"
            Assert.True(
                data.TimeSinceLastBackup == "Just now" || data.TimeSinceLastBackup.EndsWith("ago"),
                $"Expected 'Just now' or ending with 'ago', got '{data.TimeSinceLastBackup}'");
        }
    }
}
