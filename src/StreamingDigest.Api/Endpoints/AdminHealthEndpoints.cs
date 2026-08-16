using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Application.Services.Health;
using StreamingDigest.Domain.Health;
using StreamingDigest.Infrastructure.Extensions;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Services.Health;
using StreamingDigest.Web.Models;
using DomainHealthState = StreamingDigest.Domain.Health.HealthState;
using ApiHealthState = StreamingDigest.Web.Models.HealthState;

namespace StreamingDigest.Api.Endpoints;

internal static class AdminHealthEndpoints
{
    public static void MapAdminHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/health", GetAdminHealth)
            .WithName("AdminHealth")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithSummary("Get live admin health status")
            .WithDescription("Returns current health status including live model states, observability pipeline, and storage systems. Settings and storage are backed by the current runtime; backup readiness remains preview state until live verification is implemented.");
    }

    private static async Task<IResult> GetAdminHealth(
        CompositeServiceHealthProvider compositeProbe,
        UpgradeCompatibilityStateService upgradeService,
        IModelRuntimeStateRepository modelStateRepository,
        IBackupManifestChecker backupManifestChecker,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StreamingDigest.Api.Endpoints.AdminHealthEndpoints");
        logger.LogDebug("Generating live admin health snapshot");

        var connectionString = configuration.GetStreamingDigestConnectionString();

        try
        {
            var probes = await compositeProbe.ProbeAllAsync(cancellationToken);
            logger.LogDebug("Service health probes completed: {ProbeCount} services", probes.Length);

            var overallHealth = compositeProbe.ComputeOverallHealth(probes);
            logger.LogDebug("Overall health computed: {OverallHealth}", overallHealth);

            var health = new HealthResponse
            {
                Settings = await BuildSettingsSection(upgradeService, connectionString, logger, cancellationToken),
                Models = await BuildModelsSection(modelStateRepository, logger, cancellationToken),
                Observability = BuildObservabilitySection(probes, logger),
                Storage = BuildStorageSection(probes, logger),
                BackupReadiness = await BuildBackupReadinessSection(backupManifestChecker, logger, cancellationToken),
                OverallHealth = ToApiHealthState(overallHealth),
                LastUpdatedAt = DateTime.UtcNow
            };

            logger.LogDebug("Admin health snapshot generated: {OverallHealth}", health.OverallHealth);

            if (overallHealth == DomainHealthState.Error)
            {
                logger.LogDebug("Critical service failure detected; returning 503");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (overallHealth == DomainHealthState.Reconnecting)
            {
                logger.LogDebug("Service reconnecting; returning 202 Accepted with retry");
                return Results.Accepted(value: health);
            }

            return Results.Ok(health);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating admin health snapshot: {ErrorMessage}", ex.Message);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<SettingsSection> BuildSettingsSection(
        UpgradeCompatibilityStateService upgradeService,
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Building settings section from live database state");

        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.LogDebug("Connection string unavailable; returning degraded settings state");
                return new SettingsSection
                {
                    State = ApiHealthState.Degraded,
                    Summary = "Settings are not yet available because the database connection string is missing.",
                    Details = ["Database connection string is unavailable."],
                    PreviewMode = false
                };
            }

            var versionState = await upgradeService.ReadVersionStateAsync(connectionString, cancellationToken);
            var details = new List<string>();
            var state = ApiHealthState.Ready;

            if (versionState.AppVersion is null)
            {
                details.Add("App version not set");
                state = ApiHealthState.Degraded;
            }

            if (versionState.DbSchemaVersion is null)
            {
                details.Add("Database schema version not set");
                state = ApiHealthState.Degraded;
            }

            logger.LogDebug("Settings section built (live): state={State}, appVersion={AppVersion}, dbSchemaVersion={DbSchemaVersion}", state, versionState.AppVersion ?? "null", versionState.DbSchemaVersion ?? "null");

            return new SettingsSection
            {
                State = state,
                Summary = state == ApiHealthState.Ready
                    ? $"Version {versionState.AppVersion} ready"
                    : "Some settings require verification",
                Details = details,
                PreviewMode = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building settings section: {ErrorMessage}", ex.Message);
            return new SettingsSection
            {
                State = ApiHealthState.Error,
                Summary = "Error loading settings configuration",
                Details = [ex.Message],
                PreviewMode = false
            };
        }
    }

    private static async Task<ModelsSection> BuildModelsSection(
        IModelRuntimeStateRepository modelStateRepository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Building models section from live model_runtime_states");

        try
        {
            var states = await modelStateRepository.GetAllAsync(cancellationToken);
            logger.LogDebug("Loaded {ModelCount} model states", states.Count);

            var models = states
                .Select(state => new ModelHealthDetail
                {
                    Name = state.RuntimeRole.ToString(),
                    State = ParseHealthState(state.Status),
                    Status = state.Status,
                    Version = state.ModelId,
                    Details = state.LastErrorSummary is not null
                        ? $"{state.ProgressPercent}% complete; Error: {state.LastErrorSummary}"
                        : $"{state.ProgressPercent}% complete"
                })
                .ToList();

            var activeOps = states.Count(s => s.CurrentOperationId.HasValue);
            var overallState = states.Any(s => s.Status == "failed")
                ? ApiHealthState.Error
                : states.Any(s => s.Status == "ready")
                    ? ApiHealthState.Ready
                    : ApiHealthState.Degraded;

            logger.LogDebug("Models section built (live): {ModelCount} models, {ActiveOps} operations", models.Count, activeOps);

            return new ModelsSection
            {
                State = overallState,
                Summary = models.Count == 0
                    ? "No models configured"
                    : models.All(m => m.State == ApiHealthState.Ready)
                        ? "All models operational"
                        : "Some models require attention",
                Models = models,
                ActiveOperationCount = activeOps,
                PreviewMode = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building models section: {ErrorMessage}", ex.Message);
            return new ModelsSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving model status",
                Models = [],
                ActiveOperationCount = 0,
                PreviewMode = false
            };
        }
    }

    private static ObservabilitySection BuildObservabilitySection(ServiceHealthDetails[] probes, ILogger logger)
    {
        logger.LogDebug("Building observability section from service health probes");

        try
        {
            var obsProbe = probes.FirstOrDefault(p => p.ServiceName == "Observability");
            var state = obsProbe?.Status ?? DomainHealthState.Degraded;

            var details = new List<string>();
            if (obsProbe?.DetailJson is not null && obsProbe.DetailJson.TryGetValue("error", out var errorValue) && errorValue is not null)
            {
                details.Add($"Observability error: {errorValue}");
            }

            logger.LogDebug("Observability section built (live): state={State}", state);

            return new ObservabilitySection
            {
                State = ToApiHealthState(state),
                Summary = state == DomainHealthState.Ready
                    ? "Telemetry collection and export operational"
                    : $"Observability {state.ToString().ToLower()}",
                TracesStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                MetricsStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                LogsStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                Details = details,
                PreviewMode = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building observability section: {ErrorMessage}", ex.Message);
            return new ObservabilitySection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving observability status",
                Details = [ex.Message],
                PreviewMode = false
            };
        }
    }

    private static StorageSection BuildStorageSection(ServiceHealthDetails[] probes, ILogger logger)
    {
        logger.LogDebug("Building storage section from Postgres health probe");

        try
        {
            var postgresProbe = probes.FirstOrDefault(p => p.ServiceName == "Postgres");
            var state = postgresProbe?.Status ?? DomainHealthState.Error;

            var details = new List<string>();
            if (postgresProbe?.LatencyMs is not null)
            {
                details.Add($"Database latency: {postgresProbe.LatencyMs}ms");
            }

            if (postgresProbe?.DetailJson is not null && postgresProbe.DetailJson.TryGetValue("extension", out var extensionValue) && extensionValue is not null)
            {
                details.Add($"pgvector extension: {extensionValue}");
            }

            logger.LogDebug("Storage section built (live): state={State}, latency={Latency}ms", state, postgresProbe?.LatencyMs);

            return new StorageSection
            {
                State = ToApiHealthState(state),
                Summary = state == DomainHealthState.Ready ? "Storage systems operational" : $"Storage {state.ToString().ToLower()}",
                PostgresStatus = state.ToString(),
                Details = details,
                PreviewMode = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building storage section: {ErrorMessage}", ex.Message);
            return new StorageSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving storage status",
                Details = [ex.Message],
                PreviewMode = false
            };
        }
    }

    private static async Task<BackupReadinessSection> BuildBackupReadinessSection(
        IBackupManifestChecker backupManifestChecker,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Building backup readiness section from live manifest verification");

        try
        {
            var backupData = await backupManifestChecker.GetBackupReadinessAsync(cancellationToken);
            logger.LogDebug("Backup readiness check complete: healthy={IsHealthy}, status={Status}, lastBackup={LastBackup}",
                backupData.IsHealthy,
                backupData.Status,
                backupData.LastBackupAtUtc);

            var state = backupData.IsHealthy ? ApiHealthState.Ready : ApiHealthState.Degraded;

            return new BackupReadinessSection
            {
                State = state,
                Summary = backupData.IsHealthy
                    ? "Backup system operational and verified"
                    : $"Backup system requires attention: {backupData.Status}",
                LastBackupAt = backupData.LastBackupAtUtc,
                TimeSinceLastBackup = backupData.TimeSinceLastBackup,
                RetentionStatus = backupData.IsHealthy ? "Compliant" : "Attention required",
                Details = backupData.Details,
                PreviewMode = false
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building backup section: {ErrorMessage}", ex.Message);
            return new BackupReadinessSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving backup status",
                Details = [ex.Message],
                PreviewMode = false
            };
        }
    }

    private static ApiHealthState ToApiHealthState(DomainHealthState state)
        => (ApiHealthState)state;

    private static ApiHealthState ParseHealthState(string status)
        => status?.ToLowerInvariant() switch
        {
            "ready" => ApiHealthState.Ready,
            "failed" => ApiHealthState.Error,
            "paused" => ApiHealthState.Paused,
            _ => ApiHealthState.Degraded
        };
}
