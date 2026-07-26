using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public class UpgradeCompatibilityStateServiceTests
{
    private static readonly ApplicationConfiguration Configuration = new()
    {
        AppVersion = "0.8.1",
        ConfigSchemaVersion = "1.0.0",
        DeploymentSchemaVersion = "1.0.0"
    };

    [Fact]
    public void Evaluate_FlagsDeploymentMismatchAndBlocksWorkerProcessing()
    {
        var service = new UpgradeCompatibilityStateService();
        var previous = new UpgradeVersionState("0.8.0", "005", "1.0.0", "0.9.0", false);
        var current = new UpgradeVersionState("0.8.1", "006", "1.0.0", "0.9.0", false);

        var result = service.Evaluate(Configuration, previous, current, requiredDbSchemaVersion: "006");

        Assert.False(result.WorkerCanProcessJobs);
        Assert.Equal(UpgradeCategory.ComposeDeploymentMigration, result.Category);
        Assert.Equal("Manual migration required", result.RiskLevel);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("Deployment schema version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_RecognizesDataMigrationWhenDbSchemaChanges()
    {
        var service = new UpgradeCompatibilityStateService();
        var previous = new UpgradeVersionState("0.8.0", "005", "1.0.0", "1.0.0", false);
        var current = new UpgradeVersionState("0.8.1", "006", "1.0.0", "1.0.0", false);

        var result = service.Evaluate(Configuration, previous, current, requiredDbSchemaVersion: "006");

        Assert.True(result.WorkerCanProcessJobs);
        Assert.Equal(UpgradeCategory.AppUpgradeWithDataMigration, result.Category);
        Assert.True(result.BackupRecommended);
    }

    [Fact]
    public void Evaluate_RecognizesSafeAppOnlyUpgradeWhenVersionsAreAligned()
    {
        var service = new UpgradeCompatibilityStateService();
        var previous = new UpgradeVersionState("0.8.0", "006", "1.0.0", "1.0.0", false);
        var current = new UpgradeVersionState("0.8.1", "006", "1.0.0", "1.0.0", false);

        var result = service.Evaluate(Configuration, previous, current, requiredDbSchemaVersion: "006");

        Assert.True(result.WorkerCanProcessJobs);
        Assert.Equal(UpgradeCategory.SafeAppOnlyUpgrade, result.Category);
        Assert.Equal("Safe", result.RiskLevel);
        Assert.Equal("v0.8.1-deploy.1.0.0", result.ComposeTag);
    }
}
