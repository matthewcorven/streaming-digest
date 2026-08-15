namespace StreamingDigest.Web.Models;

/// <summary>
/// Live health status response for the admin health endpoint.
/// Sections marked as "Live" are backed by real runtime data via ModelStatusService.
/// Sections marked as "Preview" represent expected state but may be ahead of operational confirmation.
/// </summary>
public sealed class HealthResponse
{
    /// <summary>
    /// Settings configuration status (Live data from system configuration).
    /// </summary>
    public SettingsSection Settings { get; init; } = new();

    /// <summary>
    /// Runtime model status (Live data from ModelStatusService).
    /// </summary>
    public ModelsSection Models { get; init; } = new();

    /// <summary>
    /// Observability pipeline status (Live data from telemetry collection).
    /// </summary>
    public ObservabilitySection Observability { get; init; } = new();

    /// <summary>
    /// Database and storage status (Preview - based on last successful connection).
    /// </summary>
    public StorageSection Storage { get; init; } = new();

    /// <summary>
    /// Backup readiness (Preview - based on last backup manifest, not live verification).
    /// </summary>
    public BackupReadinessSection BackupReadiness { get; init; } = new();

    /// <summary>
    /// Overall system health determination. Ready if Settings/Models/Observability are healthy, otherwise reflects critical issue.
    /// </summary>
    public HealthState OverallHealth { get; init; } = HealthState.Ready;

    /// <summary>
    /// ISO 8601 timestamp when health data was last updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Settings configuration health (Live).
/// </summary>
public sealed class SettingsSection
{
    /// <summary>Current health state of the settings system.</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable summary of settings status.</summary>
    public string Summary { get; init; } = "Settings configuration loaded and valid.";

    /// <summary>Details about specific settings that may have issues.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>
/// Runtime models health (Live from ModelStatusService).
/// </summary>
public sealed class ModelsSection
{
    /// <summary>Overall health of model subsystem.</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable summary of model subsystem status.</summary>
    public string Summary { get; init; } = "All configured models are operational.";

    /// <summary>Per-model runtime status (e.g., chat model version, embedding model ready).</summary>
    public IReadOnlyList<ModelHealthDetail> Models { get; init; } = [];

    /// <summary>Count of models actively running operations (regeneration, updates).</summary>
    public int ActiveOperationCount { get; init; } = 0;
}

/// <summary>
/// Per-model runtime health detail.
/// </summary>
public sealed class ModelHealthDetail
{
    /// <summary>Model name/identifier (e.g., "ChatModel", "EmbeddingModel").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current model state (e.g., Ready, Degraded, Reconnecting).</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable status description.</summary>
    public string Status { get; init; } = "Operational";

    /// <summary>Loaded model version/identifier.</summary>
    public string? Version { get; init; }

    /// <summary>Optional details about ongoing operations or warnings.</summary>
    public string? Details { get; init; }
}

/// <summary>
/// Observability pipeline health (Live telemetry signals).
/// </summary>
public sealed class ObservabilitySection
{
    /// <summary>Overall observability pipeline state.</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable summary of observability status.</summary>
    public string Summary { get; init; } = "Telemetry collection and export operational.";

    /// <summary>Traces export status (OTEL receiver working).</summary>
    public string TracesStatus { get; init; } = "Operational";

    /// <summary>Metrics export status (prometheus/metrics receiver working).</summary>
    public string MetricsStatus { get; init; } = "Operational";

    /// <summary>Logs export status (structured logs being collected).</summary>
    public string LogsStatus { get; init; } = "Operational";

    /// <summary>Optional warning details if any signal is degraded.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>
/// Storage and database health (Preview - based on last verified state).
/// </summary>
public sealed class StorageSection
{
    /// <summary>Storage subsystem state.</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable storage status.</summary>
    public string Summary { get; init; } = "Storage systems operational.";

    /// <summary>PostgreSQL connection health.</summary>
    public string PostgresStatus { get; init; } = "Connected";

    /// <summary>Available disk space percentage.</summary>
    public int? DiskUsagePercent { get; init; }

    /// <summary>pgvector extension status.</summary>
    public string? VectorExtensionStatus { get; init; }

    /// <summary>Optional details about storage concerns.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>
/// Backup system readiness (Preview - based on manifest, not live verification).
/// </summary>
public sealed class BackupReadinessSection
{
    /// <summary>Backup readiness state.</summary>
    public HealthState State { get; init; } = HealthState.Ready;

    /// <summary>Human-readable backup status.</summary>
    public string Summary { get; init; } = "Backups operational and ready for recovery.";

    /// <summary>Last successful backup timestamp (ISO 8601).</summary>
    public DateTime? LastBackupAt { get; init; }

    /// <summary>Time since last backup (human-readable, e.g., "2h ago").</summary>
    public string? TimeSinceLastBackup { get; init; }

    /// <summary>Backup retention policy status.</summary>
    public string RetentionStatus { get; init; } = "Compliant";

    /// <summary>Optional details about backup concerns or next action.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}
