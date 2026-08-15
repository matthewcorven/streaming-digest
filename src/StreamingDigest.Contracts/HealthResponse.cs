namespace StreamingDigest.Web.Models;

/// <summary>
/// Live health status response for the admin health endpoint (ADR-0018).
/// Sections with <c>PreviewMode = false</c> are backed by live runtime data.
/// Sections with <c>PreviewMode = true</c> represent expected state; actual verification is pending.
/// UI must render preview signals with (?) badge + tooltip explaining why preview.
/// </summary>
public sealed class HealthResponse
{
    /// <summary>
    /// Settings configuration status (Live: app &amp; DB schema version from current deployment).
    /// </summary>
    public SettingsSection Settings { get; init; } = new();

    /// <summary>
    /// Runtime model status (Live: per-model state from database, same source as GET /api/models/status).
    /// </summary>
    public ModelsSection Models { get; init; } = new();

    /// <summary>
    /// Observability pipeline status (Live: telemetry probe results).
    /// </summary>
    public ObservabilitySection Observability { get; init; } = new();

    /// <summary>
    /// Database and storage status (Live: Postgres connectivity probe results).
    /// </summary>
    public StorageSection Storage { get; init; } = new();

    /// <summary>
    /// Backup readiness (Preview: static expected state; actual manifest check pending #271).
    /// </summary>
    public BackupReadinessSection BackupReadiness { get; init; } = new();

    /// <summary>
    /// Overall system health determination. Ready if all live signals (Settings/Models/Observability/Storage) are healthy.
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

    /// <summary>
    /// False: live data from database. True: preview/static data (ADR-0018).
    /// </summary>
    public bool PreviewMode { get; init; } = false;
}

/// <summary>
/// Runtime models health (Live from IModelRuntimeStateRepository, same source as GET /api/models/status).
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

    /// <summary>
    /// False: live data from model_runtime_states table. True: preview/static data (ADR-0018).
    /// </summary>
    public bool PreviewMode { get; init; } = false;
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

    /// <summary>
    /// False: live data from service health probes. True: preview/static data (ADR-0018).
    /// </summary>
    public bool PreviewMode { get; init; } = false;
}

/// <summary>
/// Storage and database health (Live probe results).
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

    /// <summary>
    /// False: live data from service health probes. True: preview/static data (ADR-0018).
    /// </summary>
    public bool PreviewMode { get; init; } = false;
}

/// <summary>
/// Backup system readiness (Preview - expected state; actual manifest verification pending #271).
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

    /// <summary>
    /// Always true: preview/static data. Live backup manifest check pending #271 (ADR-0018).
    /// </summary>
    public bool PreviewMode { get; init; } = true;
}
