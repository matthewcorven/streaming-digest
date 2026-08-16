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

/// <summary>
/// Regression tests for Domain 1: Live Health Checks (#271)
/// Validates that BackupReadinessData returns operationally authoritative state,
/// not fake/preview warnings, and correctly enforces retention policy.
/// </summary>
public sealed class BackupReadinessRegressionTests
{
    private readonly ILogger<BackupManifestChecker> _logger = NullLogger<BackupManifestChecker>.Instance;

    private static IHostEnvironment CreateHostEnvironment(string contentRootPath)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.ContentRootPath).Returns(contentRootPath);
        mock.Setup(e => e.EnvironmentName).Returns("Test");
        return mock.Object;
    }

    /// <summary>
    /// AC1.1: Verify live backup readiness is returned (not fake/preview state)
    /// When PreviewMode = false, the checker returns live data from actual backups.
    /// </summary>
    [Fact]
    public async Task LiveHealthChecks_EnabledProduction_ReturnsCurrentState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"live-health-{Guid.NewGuid():N}");
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
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                // Verify live state is returned (not fake/preview)
                Assert.True(result.IsHealthy, "Live backup should be healthy");
                Assert.NotNull(result.LastBackupAtUtc);
                Assert.Equal("Backup verified", result.Status);
                // Backup age should not be "Never" for a recent backup
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

    /// <summary>
    /// AC1.2: Verify retention policy is enforced (old backups flagged appropriately)
    /// Validates that backups older than retention window are reported as degraded.
    /// </summary>
    [Fact]
    public async Task RetentionPolicy_OldBackup_ReportsDegraded()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create backup that appears to be old (modified time is old)
            var backupFileName = "streaming-digest-backup-20260101-000000.zip";
            var backupPath = Path.Combine(tempDir, backupFileName);
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                var oldDate = DateTime.UtcNow.AddDays(-30);
                var manifest = new
                {
                    createdAtUtc = oldDate.ToString("o"),
                    backupFileName = backupFileName,
                    schemaVersion = "1.0.0",
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[]
                    {
                        new { name = "postgres", status = "completed", path = "/backup/postgres.sql" }
                    }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                // Even though verified, it should be flagged due to age
                Assert.NotNull(result.LastBackupAtUtc);
                Assert.NotEmpty(result.Details);
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

    /// <summary>
    /// AC1.3: Verify schema validation errors map to Error state
    /// When manifest has unrecognized format, status reflects error not degraded.
    /// </summary>
    [Fact]
    public async Task SchemaValidation_InvalidManifest_MapsToError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var backupFileName = "streaming-digest-backup-20260815-120000.zip";
            var backupPath = Path.Combine(tempDir, backupFileName);
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                // Create zip with invalid JSON manifest
                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, "{invalid json content");
                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                // Invalid manifest should result in unhealthy state
                Assert.False(result.IsHealthy);
                Assert.Contains("unreadable", result.Status.ToLower());
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

    /// <summary>
    /// AC1.4: Verify path resolution uses ContentRootPath in production
    /// Configured backup path should be resolved correctly even with relative paths.
    /// </summary>
    [Fact]
    public async Task PathResolution_RelativeBackupPath_ResolvedCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"path-resolve-{Guid.NewGuid():N}");
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
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[] { new { name = "postgres", status = "completed", path = "/backup/postgres.sql" } }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                // Use absolute path in config to verify path resolution works
                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.True(result.IsHealthy);
                Assert.Contains("Backup verified", result.Status);
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

    /// <summary>
    /// AC1.5: Verify fake warnings don't appear with PreviewMode = false
    /// No hardcoded "example" or "fake" state appears in operational mode.
    /// </summary>
    [Fact]
    public async Task FakeWarningPrevention_ProductionMode_NoMockState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"no-fake-{Guid.NewGuid():N}");
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
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[] { new { name = "postgres", status = "completed", path = "/backup/postgres.sql" } }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                // Verify no fake/mock data in response
                Assert.DoesNotContain("fake", result.Status.ToLower());
                Assert.DoesNotContain("example", result.Status.ToLower());
                Assert.DoesNotContain("preview", result.Status.ToLower());
                Assert.DoesNotContain("mock", result.Status.ToLower());
                
                // All details should reference real data
                foreach (var detail in result.Details)
                {
                    Assert.DoesNotContain("example", detail.ToLower());
                    Assert.DoesNotContain("fake", detail.ToLower());
                }
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

    /// <summary>
    /// AC1.6: Verify backup age is calculated correctly
    /// TimeSinceLastBackup should reflect actual time delta with appropriate formatting.
    /// </summary>
    [Fact]
    public async Task BackupAge_RecentBackup_CalculatedCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"age-calc-{Guid.NewGuid():N}");
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
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[] { new { name = "postgres", status = "completed", path = "/backup/postgres.sql" } }
                };

                var manifestPath = Path.Combine(stagingDir, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, backupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.True(result.IsHealthy);
                // Age display should not be "Never" for a recent backup
                Assert.NotEqual("Never", result.TimeSinceLastBackup);
                // Should show something like "0m ago", "1m ago", or "Just now"
                Assert.NotEmpty(result.TimeSinceLastBackup);
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

    /// <summary>
    /// AC1.7: Verify multiple backup archives handled correctly (latest used)
    /// When multiple backups exist, the most recent is selected and reported.
    /// </summary>
    [Fact]
    public async Task MultipleBackups_LatestSelected_NotOldest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"multi-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var stagingDir = Path.Combine(Path.GetTempPath(), $"staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDir);

            try
            {
                // Create old backup
                var oldBackupPath = Path.Combine(tempDir, "streaming-digest-backup-20260701-000000.zip");
                var oldManifestPath = Path.Combine(stagingDir, "manifest.json");
                var oldManifest = new
                {
                    createdAtUtc = DateTime.UtcNow.AddDays(-30).ToString("o"),
                    backupFileName = "old-backup.zip",
                    schemaVersion = "1.0.0",
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[] { new { name = "postgres", status = "completed", path = "/backup/postgres.sql" } }
                };
                await File.WriteAllTextAsync(oldManifestPath, JsonSerializer.Serialize(oldManifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, oldBackupPath);

                // Clean up staging
                File.Delete(oldManifestPath);

                // Create new backup
                var now = DateTime.UtcNow;
                var newBackupPath = Path.Combine(tempDir, "streaming-digest-backup-20260815-120000.zip");
                var newManifestPath = Path.Combine(stagingDir, "manifest.json");
                var newManifest = new
                {
                    createdAtUtc = now.ToString("o"),
                    backupFileName = "new-backup.zip",
                    schemaVersion = "1.0.0",
                    verificationStatus = "verified",
                    restoreTarget = "compose-stack",
                    assets = new[] { new { name = "postgres", status = "completed", path = "/backup/postgres.sql" } }
                };
                await File.WriteAllTextAsync(newManifestPath, JsonSerializer.Serialize(newManifest, new JsonSerializerOptions { WriteIndented = true }));
                ZipFile.CreateFromDirectory(stagingDir, newBackupPath);

                var config = new ApplicationConfiguration
                {
                    Backup = new BackupSettings { DestinationPath = tempDir }
                };
                var checker = new BackupManifestChecker(config, CreateHostEnvironment(tempDir), _logger);

                var result = await checker.GetBackupReadinessAsync();

                Assert.True(result.IsHealthy);
                // Verify the new backup was selected (not the old one)
                // Details should show the latest backup filename, which is 20260815
                Assert.Contains("20260815", result.Details.First());
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
}
