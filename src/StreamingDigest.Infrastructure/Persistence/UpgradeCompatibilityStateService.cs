using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class UpgradeCompatibilityStateService(ILogger? logger = null)
{
    private const string AppVersionKey = "system.appVersion";
    private const string DbSchemaVersionKey = "system.dbSchemaVersion";
    private const string ConfigSchemaVersionKey = "system.configSchemaVersion";
    private const string DeploymentSchemaVersionKey = "system.deploymentSchemaVersion";
    private const string DerivedDataRegenerationRequiredKey = "system.derivedDataRegenerationRequired";

    public async Task<UpgradeVersionState> ReadVersionStateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required to read upgrade compatibility state.", nameof(connectionString));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            var appVersion = await AppSettingReader.ReadStringAsync(connection, AppVersionKey, string.Empty, cancellationToken);
            var dbSchemaVersion = await AppSettingReader.ReadStringAsync(connection, DbSchemaVersionKey, string.Empty, cancellationToken);
            var configSchemaVersion = await AppSettingReader.ReadStringAsync(connection, ConfigSchemaVersionKey, string.Empty, cancellationToken);
            var deploymentSchemaVersion = await AppSettingReader.ReadStringAsync(connection, DeploymentSchemaVersionKey, string.Empty, cancellationToken);
            var derivedDataRegenerationRequired = await AppSettingReader.ReadBoolAsync(connection, DerivedDataRegenerationRequiredKey, false, cancellationToken);

            return new UpgradeVersionState(
                string.IsNullOrWhiteSpace(appVersion) ? null : appVersion,
                string.IsNullOrWhiteSpace(dbSchemaVersion) ? null : dbSchemaVersion,
                string.IsNullOrWhiteSpace(configSchemaVersion) ? null : configSchemaVersion,
                string.IsNullOrWhiteSpace(deploymentSchemaVersion) ? null : deploymentSchemaVersion,
                derivedDataRegenerationRequired);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P01", StringComparison.Ordinal))
        {
            logger?.LogInformation("Upgrade compatibility state table is not available yet; returning empty version state.");
            return new UpgradeVersionState(null, null, null, null, false);
        }
    }

    public async Task PersistRuntimeStateAsync(
        string connectionString,
        ApplicationConfiguration configuration,
        string dbSchemaVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required to persist upgrade compatibility state.", nameof(connectionString));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(dbSchemaVersion))
        {
            throw new ArgumentException("The database schema version is required.", nameof(dbSchemaVersion));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await UpsertSettingAsync(connection, AppVersionKey, configuration.AppVersion, seedOnly: false);
        await UpsertSettingAsync(connection, DbSchemaVersionKey, dbSchemaVersion, seedOnly: false);
        await UpsertSettingAsync(connection, ConfigSchemaVersionKey, configuration.ConfigSchemaVersion, seedOnly: false);
        await UpsertSettingAsync(connection, DeploymentSchemaVersionKey, configuration.DeploymentSchemaVersion, seedOnly: true);
        await UpsertSettingAsync(connection, DerivedDataRegenerationRequiredKey, false, seedOnly: true);
    }

    public UpgradeCompatibilityEvaluation Evaluate(
        ApplicationConfiguration configuration,
        UpgradeVersionState previousState,
        UpgradeVersionState currentState,
        string requiredDbSchemaVersion)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (previousState is null)
        {
            throw new ArgumentNullException(nameof(previousState));
        }

        if (currentState is null)
        {
            throw new ArgumentNullException(nameof(currentState));
        }

        if (string.IsNullOrWhiteSpace(requiredDbSchemaVersion))
        {
            throw new ArgumentException("A required database schema version is required for compatibility evaluation.", nameof(requiredDbSchemaVersion));
        }

        var blockers = new List<string>();
        var dbState = currentState.DbSchemaVersion;
        var configState = currentState.ConfigSchemaVersion;
        var deploymentState = currentState.DeploymentSchemaVersion;

        if (string.IsNullOrWhiteSpace(dbState))
        {
            blockers.Add("Database schema version is unknown. Startup cannot confirm compatibility.");
        }
        else if (CompareVersions(dbState, requiredDbSchemaVersion) > 0)
        {
            blockers.Add($"Database schema version '{dbState}' is newer than this application supports ('{requiredDbSchemaVersion}').");
        }

        if (string.IsNullOrWhiteSpace(configState))
        {
            blockers.Add("Configuration schema version is unknown. Startup cannot confirm compatibility.");
        }
        else if (CompareVersions(configState, configuration.ConfigSchemaVersion) > 0)
        {
            blockers.Add($"Configuration schema version '{configState}' is newer than this application supports ('{configuration.ConfigSchemaVersion}').");
        }

        var deploymentMigrationRequired = false;
        if (string.IsNullOrWhiteSpace(deploymentState))
        {
            blockers.Add("Deployment schema version is unknown. Startup cannot confirm Compose/deployment compatibility.");
        }
        else if (CompareVersions(deploymentState, configuration.DeploymentSchemaVersion) < 0)
        {
            deploymentMigrationRequired = true;
            blockers.Add($"Deployment schema version '{deploymentState}' is behind the required version '{configuration.DeploymentSchemaVersion}'.");
        }

        var hasDataMigration = !string.Equals(previousState.DbSchemaVersion, currentState.DbSchemaVersion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousState.ConfigSchemaVersion, currentState.ConfigSchemaVersion, StringComparison.OrdinalIgnoreCase);

        var category = ResolveCategory(blockers, deploymentMigrationRequired, hasDataMigration, currentState.DerivedDataRegenerationRequired);
        var riskLevel = ResolveRiskLevel(category);
        var backupRequired = category is UpgradeCategory.HighRiskInfrastructureMigration;
        var backupRecommended = backupRequired
            || category is UpgradeCategory.AppUpgradeWithDataMigration
            || category is UpgradeCategory.AppUpgradeWithDerivedDataRegeneration
            || category is UpgradeCategory.ComposeDeploymentMigration;
        var workerCanProcessJobs = blockers.Count == 0;
        var summary = BuildSummary(category, backupRequired, backupRecommended, blockers);
        var composeTag = $"v{configuration.AppVersion}-deploy.{configuration.DeploymentSchemaVersion}";

        return new UpgradeCompatibilityEvaluation(
            workerCanProcessJobs,
            category,
            riskLevel,
            backupRecommended,
            backupRequired,
            summary,
            composeTag,
            blockers);
    }

    public UpgradeCompatibilityEvaluation BuildDatabaseUnavailableEvaluation()
    {
        return new UpgradeCompatibilityEvaluation(
            false,
            UpgradeCategory.HighRiskInfrastructureMigration,
            "Critical",
            true,
            true,
            "Database connectivity is unavailable. Worker job processing remains paused until compatibility can be verified.",
            string.Empty,
            ["Database connectivity is unavailable; compatibility checks could not run."]);
    }

    private static UpgradeCategory ResolveCategory(
        IReadOnlyCollection<string> blockers,
        bool deploymentMigrationRequired,
        bool hasDataMigration,
        bool derivedDataRegenerationRequired)
    {
        var hasForwardIncompatibilityBlocker = blockers.Any(blocker =>
            blocker.Contains("newer than this application supports", StringComparison.OrdinalIgnoreCase));
        if (hasForwardIncompatibilityBlocker)
        {
            return UpgradeCategory.HighRiskInfrastructureMigration;
        }

        if (deploymentMigrationRequired)
        {
            return UpgradeCategory.ComposeDeploymentMigration;
        }

        if (hasDataMigration)
        {
            return UpgradeCategory.AppUpgradeWithDataMigration;
        }

        if (derivedDataRegenerationRequired)
        {
            return UpgradeCategory.AppUpgradeWithDerivedDataRegeneration;
        }

        return UpgradeCategory.SafeAppOnlyUpgrade;
    }

    private static string ResolveRiskLevel(UpgradeCategory category)
        => category switch
        {
            UpgradeCategory.SafeAppOnlyUpgrade => "Safe",
            UpgradeCategory.AppUpgradeWithDataMigration => "Backup recommended",
            UpgradeCategory.AppUpgradeWithDerivedDataRegeneration => "Backup recommended",
            UpgradeCategory.ComposeDeploymentMigration => "Manual migration required",
            UpgradeCategory.HighRiskInfrastructureMigration => "Backup required",
            _ => "Backup recommended"
        };

    private static string BuildSummary(
        UpgradeCategory category,
        bool backupRequired,
        bool backupRecommended,
        IReadOnlyCollection<string> blockers)
    {
        if (blockers.Count > 0)
        {
            return $"{string.Join(" ", blockers)} Worker job processing is paused until compatibility checks pass.";
        }

        return category switch
        {
            UpgradeCategory.SafeAppOnlyUpgrade => "Safe app-only upgrade path. No deployment migration is required.",
            UpgradeCategory.AppUpgradeWithDataMigration => "App + data migration path. Apply DB/config migration before resuming workers.",
            UpgradeCategory.AppUpgradeWithDerivedDataRegeneration => "Derived-data regeneration path. Regenerate stale derived data before validating search quality.",
            UpgradeCategory.ComposeDeploymentMigration => "Compose/deployment migration required. Confirm deployment topology updates before resuming workers.",
            UpgradeCategory.HighRiskInfrastructureMigration => "High-risk infrastructure migration path. Backup is required before proceeding.",
            _ => backupRequired
                ? "Backup required before applying migration steps."
                : backupRecommended
                    ? "Backup recommended before applying migration steps."
                    : "Compatibility checks passed."
        };
    }

    private static int CompareVersions(string left, string right)
    {
        if (TryParseNumericVersion(left, out var leftNumeric) && TryParseNumericVersion(right, out var rightNumeric))
        {
            return leftNumeric.CompareTo(rightNumeric);
        }

        if (Version.TryParse(left, out var leftSemVer) && Version.TryParse(right, out var rightSemVer))
        {
            return leftSemVer.CompareTo(rightSemVer);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNumericVersion(string value, out int numericVersion)
    {
        if (int.TryParse(value, out numericVersion))
        {
            return true;
        }

        var digitsOnly = new string(value.Where(char.IsDigit).ToArray());
        return digitsOnly.Length > 0 && int.TryParse(digitsOnly, out numericVersion);
    }

    private async Task UpsertSettingAsync(NpgsqlConnection connection, string key, object value, bool seedOnly)
    {
        var serializedValue = JsonSerializer.Serialize(value);
        var sql = seedOnly
            ? """
              INSERT INTO public.app_settings (key, value_json, updated_at)
              VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)
              ON CONFLICT (key) DO NOTHING;
              """
            : """
              INSERT INTO public.app_settings (key, value_json, updated_at)
              VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)
              ON CONFLICT (key) DO UPDATE SET
                  value_json = EXCLUDED.value_json,
                  updated_at = EXCLUDED.updated_at;
              """;

        try
        {
            await connection.ExecuteAsync(sql, new { key, valueJson = serializedValue });
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to persist upgrade compatibility state key {StateKey}", key);
            throw;
        }
    }
}

public enum UpgradeCategory
{
    SafeAppOnlyUpgrade,
    AppUpgradeWithDataMigration,
    AppUpgradeWithDerivedDataRegeneration,
    ComposeDeploymentMigration,
    HighRiskInfrastructureMigration
}

public sealed record UpgradeVersionState(
    string? AppVersion,
    string? DbSchemaVersion,
    string? ConfigSchemaVersion,
    string? DeploymentSchemaVersion,
    bool DerivedDataRegenerationRequired);

public sealed record UpgradeCompatibilityEvaluation(
    bool WorkerCanProcessJobs,
    UpgradeCategory Category,
    string RiskLevel,
    bool BackupRecommended,
    bool BackupRequired,
    string Summary,
    string ComposeTag,
    IReadOnlyList<string> Blockers);
