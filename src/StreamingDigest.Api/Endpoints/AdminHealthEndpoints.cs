using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Services.Health;
using StreamingDigest.Domain.Health;
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
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StreamingDigest.Api.Endpoints.AdminHealthEndpoints");
        logger.LogDebug("Generating live admin health snapshot");

        var connectionString = configuration.GetConnectionString("streamingdigest")
            ?? configuration.GetConnectionString("postgres")
            ?? configuration.GetConnectionString("Default")
            ?? string.Empty;

        try
        {
            var probes = await compositeProbe.ProbeAllAsync(cancellationToken);
            logger.LogDebug("Service health probes completed: {ProbeCount} services", probes.Length);

            var overallHealth = compositeProbe.ComputeOverallHealth(probes);
            logger.LogDebug("Overall health computed: {OverallHealth}", overallHealth);

            var health = new HealthResponse
            {
                Settings = await BuildSettingsSection(upgradeService, connectionString, logger, cancellationToken),
                Models = BuildModelsSection(logger),
                Observability = BuildObservabilitySection(probes, logger),
                Storage = BuildStorageSection(probes, logger),
                BackupReadiness = await BuildBackupReadinessSection(logger, cancellationToken),
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
        logger.LogDebug("Building settings section");

        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.LogDebug("Connection string unavailable; returning degraded settings state");
                return new SettingsSection
                {
                    State = ApiHealthState.Degraded,
                    Summary = "Settings are not yet available because the database connection string is missing.",
                    Details = ["Database connection string is unavailable."]
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

            logger.LogDebug("Settings section built: state={State}, appVersion={AppVersion}, dbSchemaVersion={DbSchemaVersion}", state, versionState.AppVersion ?? "null", versionState.DbSchemaVersion ?? "null");

            return new SettingsSection
            {
                State = state,
                Summary = state == ApiHealthState.Ready
                    ? $"Version {versionState.AppVersion} ready"
                    : "Some settings require verification",
                Details = details
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building settings section: {ErrorMessage}", ex.Message);
            return new SettingsSection
            {
                State = ApiHealthState.Error,
                Summary = "Error loading settings configuration",
                Details = [ex.Message]
            };
        }
    }

    private static ModelsSection BuildModelsSection(ILogger logger)
    {
        logger.LogDebug("Building models section");

        try
        {
            logger.LogWarning("Models section is preview state; runtime model status integration is still pending");
            return new ModelsSection
            {
                State = ApiHealthState.Ready,
                Summary = "Model status service integration pending (preview state)",
                Models = Array.Empty<ModelHealthDetail>(),
                ActiveOperationCount = 0
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error building models section");
            return new ModelsSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving model status",
                Models = Array.Empty<ModelHealthDetail>(),
                ActiveOperationCount = 0
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

            logger.LogDebug("Observability section built: state={State}", state);

            return new ObservabilitySection
            {
                State = ToApiHealthState(state),
                Summary = state == DomainHealthState.Ready
                    ? "Telemetry collection and export operational"
                    : $"Observability {state.ToString().ToLower()}",
                TracesStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                MetricsStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                LogsStatus = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                Details = details
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building observability section: {ErrorMessage}", ex.Message);
            return new ObservabilitySection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving observability status",
                Details = [ex.Message]
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

            logger.LogDebug("Storage section built: state={State}, latency={Latency}ms", state, postgresProbe?.LatencyMs);

            return new StorageSection
            {
                State = ToApiHealthState(state),
                Summary = state == DomainHealthState.Ready ? "Storage systems operational" : $"Storage {state.ToString().ToLower()}",
                PostgresStatus = state.ToString(),
                Details = details
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building storage section: {ErrorMessage}", ex.Message);
            return new StorageSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving storage status",
                Details = [ex.Message]
            };
        }
    }

    private static async Task<BackupReadinessSection> BuildBackupReadinessSection(
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Building backup readiness section");

        try
        {
            await Task.CompletedTask;
            logger.LogDebug("Backup readiness section built (preview state)");

            return new BackupReadinessSection
            {
                State = ApiHealthState.Ready,
                Summary = "Backup system operational (live verification pending)",
                LastBackupAt = null,
                TimeSinceLastBackup = "Unknown",
                RetentionStatus = "Not yet verified",
                Details = ["Backup verification is preview state; live verification is pending."]
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error building backup section: {ErrorMessage}", ex.Message);
            return new BackupReadinessSection
            {
                State = ApiHealthState.Error,
                Summary = "Error retrieving backup status",
                Details = [ex.Message]
            };
        }
    }

    private static ApiHealthState ToApiHealthState(DomainHealthState state)
        => (ApiHealthState)state;
}
