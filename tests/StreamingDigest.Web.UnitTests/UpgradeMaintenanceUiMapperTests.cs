using StreamingDigest.Web.Models;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public class UpgradeMaintenanceUiMapperTests
{
    [Fact]
    public void Create_MapsBackupRequiredRiskToAWarningBadgeAndBackupAction()
    {
        var snapshot = new UpgradeMaintenanceSnapshot
        {
            Risk = new RiskAssessment
            {
                Level = "Backup required",
                RequiredNextAction = "Create a backup before applying the deployment migration."
            }
        };

        var uiState = UpgradeMaintenanceUiMapper.Create(snapshot);

        Assert.Equal("Backup required", uiState.RiskLevel);
        Assert.Equal("Create a backup before applying the deployment migration.", uiState.RequiredNextAction);
        Assert.Equal("status-warning", uiState.RiskBadgeClass);
    }

    [Fact]
    public void Create_UsesFallbackActionWhenRiskActionIsMissing()
    {
        var snapshot = new UpgradeMaintenanceSnapshot
        {
            Risk = new RiskAssessment
            {
                Level = "Manual migration required"
            }
        };

        var uiState = UpgradeMaintenanceUiMapper.Create(snapshot);

        Assert.Equal("Manual migration required", uiState.RiskLevel);
        Assert.Equal("Open the runbook and confirm the migration plan before proceeding.", uiState.RequiredNextAction);
        Assert.Equal("status-danger", uiState.RiskBadgeClass);
    }
}
