namespace StreamingDigest.Web.Models;

public sealed class UpgradeMaintenanceSnapshot
{
    public string Title { get; init; } = "Upgrade & Maintenance";

    public string Summary { get; init; } = "Review current versions, migration readiness, backup health, service compatibility, and the next required action before you upgrade.";

    public VersionState Versions { get; init; } = new();

    public UpgradeState Upgrade { get; init; } = new();

    public BackupState Backup { get; init; } = new();

    public IReadOnlyList<MigrationPreviewItem> MigrationPreview { get; init; } = [];

    public IReadOnlyList<ServiceCompatibility> Services { get; init; } = [];

    public DerivedDataStatus DerivedData { get; init; } = new();

    public RiskAssessment Risk { get; init; } = new();

    public IReadOnlyList<ChecklistItem> Checklist { get; init; } = [];
}

public sealed class VersionState
{
    public string AppVersion { get; init; } = "v0.7.0";

    public string DbSchemaVersion { get; init; } = "db-2026.07.21";

    public string ConfigSchemaVersion { get; init; } = "config-2026.07.21";

    public string DeploymentSchemaVersion { get; init; } = "deploy-2026.07.21";

    public string CompatibilitySummary { get; init; } = "DB and config schemas are aligned with the current app build.";
}

public sealed class UpgradeState
{
    public string Status { get; init; } = "Migration available";

    public string Summary { get; init; } = "A deployment migration is pending and the admin UI should guide the operator through it.";
}

public sealed class BackupState
{
    public string LastBackupAt { get; init; } = "2026-07-24T16:30:00Z";

    public string Location { get; init; } = "/var/backups/streaming-digest";

    public string Health { get; init; } = "Healthy";

    public string Summary { get; init; } = "The latest backup completed successfully and verified the packed bundle.";
}

public sealed class MigrationPreviewItem
{
    public string Label { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Status { get; init; } = "Pending";

    public string Details { get; init; } = string.Empty;
}

public sealed class ServiceCompatibility
{
    public string Name { get; init; } = string.Empty;

    public string Status { get; init; } = "Healthy";

    public bool IsRequired { get; init; } = true;

    public string Summary { get; init; } = string.Empty;
}

public sealed class DerivedDataStatus
{
    public int StaleEmbeddings { get; init; }

    public int StaleSearchDocuments { get; init; }

    public int PendingSegmentApprovals { get; init; }

    public int IndexRebuilds { get; init; }

    public string Summary { get; init; } = "Everything is current for the active model and ingestion run.";
}

public sealed class RiskAssessment
{
    public string Level { get; init; } = "Backup recommended";

    public string RequiredNextAction { get; init; } = string.Empty;
}

public sealed class ChecklistItem
{
    public string Label { get; init; } = string.Empty;

    public string Status { get; init; } = "Pending";

    public string Details { get; init; } = string.Empty;
}
