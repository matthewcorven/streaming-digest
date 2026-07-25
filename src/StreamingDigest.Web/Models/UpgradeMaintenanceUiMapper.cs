using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Models;

public static class UpgradeMaintenanceUiMapper
{
    public static UpgradeMaintenanceUiState Create(UpgradeMaintenanceSnapshot snapshot)
    {
        var riskLevel = snapshot.Risk?.Level ?? string.Empty;
        var normalizedRisk = riskLevel.Trim().ToLowerInvariant();
        var requiredNextAction = string.IsNullOrWhiteSpace(snapshot.Risk?.RequiredNextAction)
            ? ResolveFallbackNextAction(normalizedRisk)
            : snapshot.Risk.RequiredNextAction;

        return new UpgradeMaintenanceUiState
        {
            RiskLevel = riskLevel,
            RequiredNextAction = requiredNextAction,
            RiskBadgeClass = normalizedRisk switch
            {
                "safe" => "status-success",
                "backup recommended" => "status-warning",
                "backup required" => "status-warning",
                "manual migration required" => "status-danger",
                "critical" => "status-danger",
                _ => "status-neutral"
            }
        };
    }

    private static string ResolveFallbackNextAction(string riskLevel)
        => riskLevel switch
        {
            "manual migration required" => "Open the runbook and confirm the migration plan before proceeding.",
            "backup required" => "Create a backup before applying the upgrade.",
            "backup recommended" => "Create a backup if the migration is not fully validated.",
            "safe" => "Continue with the upgrade and verify the post-upgrade checklist.",
            _ => "Review the migration preview and choose the next safe action."
        };
}

public sealed class UpgradeMaintenanceUiState
{
    public string RiskLevel { get; init; } = string.Empty;

    public string RequiredNextAction { get; init; } = string.Empty;

    public string RiskBadgeClass { get; init; } = "status-neutral";
}
