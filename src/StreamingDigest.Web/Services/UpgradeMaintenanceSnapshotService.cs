using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

public sealed class UpgradeMaintenanceSnapshotService
{
    public Task<UpgradeMaintenanceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new UpgradeMaintenanceSnapshot
        {
            Versions = new VersionState
            {
                AppVersion = "v0.8.1",
                DbSchemaVersion = "db-2026.07.24",
                ConfigSchemaVersion = "config-2026.07.24",
                DeploymentSchemaVersion = "deploy-2026.07.24",
                CompatibilitySummary = "The app build is aligned with the DB and config schema; deployment metadata still needs a manual review."
            },
            Upgrade = new UpgradeState
            {
                Status = "Deployment update required",
                Summary = "A deployment migration is pending for the new observability profile and volume layout."
            },
            Backup = new BackupState
            {
                LastBackupAt = "2026-07-24T16:30:00Z",
                Location = "/var/backups/streaming-digest",
                Health = "Healthy",
                Summary = "The latest backup completed successfully and verified the archive contents."
            },
            MigrationPreview =
            [
                new MigrationPreviewItem { Label = "DB schema migration", Kind = "Database", Status = "Pending", Details = "Apply the new app settings table and update the migration runner." },
                new MigrationPreviewItem { Label = "Config schema migration", Kind = "Config", Status = "Pending", Details = "Normalize the observability profile and add the new backup path setting." },
                new MigrationPreviewItem { Label = "Derived-data invalidation", Kind = "Derived data", Status = "Queued", Details = "Search documents and embeddings are marked stale until the reprocess job completes." }
            ],
            Services =
            [
                new ServiceCompatibility { Name = "API", Status = "Healthy", IsRequired = true, Summary = "Serving the admin operations API and the latest dashboard routes." },
                new ServiceCompatibility { Name = "Worker", Status = "Healthy", IsRequired = true, Summary = "Waiting on the new migration completion before resuming ingestion work." },
                new ServiceCompatibility { Name = "Scraper", Status = "Degraded", IsRequired = false, Summary = "The scraper profile is healthy but the optional queue is paused until the deployment migration completes." },
                new ServiceCompatibility { Name = "Matrix", Status = "Healthy", IsRequired = false, Summary = "The notifier is healthy and the last send verification passed." },
                new ServiceCompatibility { Name = "Postgres", Status = "Healthy", IsRequired = true, Summary = "The database is accepting reads and writes and the extension schema is current." },
                new ServiceCompatibility { Name = "Observability", Status = "Warning", IsRequired = false, Summary = "The new profile is available but the dashboard routing still needs a quick verification pass." }
            ],
            DerivedData = new DerivedDataStatus
            {
                StaleEmbeddings = 9,
                StaleSearchDocuments = 41,
                PendingSegmentApprovals = 3,
                IndexRebuilds = 1,
                Summary = "Search documents and recent embeddings are stale until the reprocess job finishes."
            },
            Risk = new RiskAssessment
            {
                Level = "Backup required",
                RequiredNextAction = "Create a backup before applying the deployment migration."
            },
            Checklist =
            [
                new ChecklistItem { Label = "Login", Status = "Ready", Details = "Admin sign-in and CSRF flow remain available." },
                new ChecklistItem { Label = "Search", Status = "Pending", Details = "Verify search results after the reprocess job completes." },
                new ChecklistItem { Label = "Screenshots", Status = "Pending", Details = "Confirm the new backup path still resolves images correctly." },
                new ChecklistItem { Label = "Matrix send", Status = "Ready", Details = "The notifier status check passed." },
                new ChecklistItem { Label = "Embedding test", Status = "Pending", Details = "Rebuild the affected embeddings and compare the latest model output." }
            ]
        };

        return Task.FromResult(snapshot);
    }
}
