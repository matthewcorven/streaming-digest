using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Application.Services.Health;
using StreamingDigest.Domain.Health;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Services.Health;
using StreamingDigest.Web.Models;
using DomainHealthState = StreamingDigest.Domain.Health.HealthState;
using ApiHealthState = StreamingDigest.Web.Models.HealthState;

namespace StreamingDigest.Api.Services;

/// <summary>
/// Publishes health status changes as server-sent events (SSE).
/// Subscribes to model status changes and broadcasts them to all connected clients.
/// Guarantees that status transitions (readiness, reconnect, warning, error) reach clients
/// without lag or stale state.
/// </summary>
public sealed class HealthStreamService : IAsyncDisposable
{
    private readonly ILogger<HealthStreamService> _logger;
    private readonly IModelRuntimeStateRepository _modelStateRepository;
    private readonly IBackupManifestChecker _backupManifestChecker;
    private readonly CompositeServiceHealthProvider _compositeProbe;
    private readonly UpgradeCompatibilityStateService _upgradeService;
    private readonly IConfiguration _configuration;
    
    private readonly List<PendingSubscriber> _subscribers = [];
    private readonly object _subscribersLock = new();
    private CancellationTokenSource? _disposeCts;
    private Task? _pollingTask;
    private object? _lastKnownState;

    private sealed record PendingSubscriber(
        StreamWriter Writer,
        CancellationToken CancellationToken,
        Guid Id);

    public HealthStreamService(
        ILogger<HealthStreamService> logger,
        IModelRuntimeStateRepository modelStateRepository,
        IBackupManifestChecker backupManifestChecker,
        CompositeServiceHealthProvider compositeProbe,
        UpgradeCompatibilityStateService upgradeService,
        IConfiguration configuration)
    {
        _logger = logger;
        _modelStateRepository = modelStateRepository;
        _backupManifestChecker = backupManifestChecker;
        _compositeProbe = compositeProbe;
        _upgradeService = upgradeService;
        _configuration = configuration;
        _disposeCts = new CancellationTokenSource();
    }

    /// <summary>
    /// Subscribes a client to the SSE stream and sends initial status snapshot.
    /// Returns immediately after subscription is registered; stream continues in background.
    /// </summary>
    public async Task SubscribeAsync(StreamWriter responseWriter, CancellationToken cancellationToken)
    {
        var subscriberId = Guid.NewGuid();
        _logger.LogDebug("SSE subscriber connecting: {SubscriberId}", subscriberId);

        try
        {
            lock (_subscribersLock)
            {
                _subscribers.Add(new PendingSubscriber(responseWriter, cancellationToken, subscriberId));
            }

            // Send initial status snapshot to rehydrate client on connect/reconnect
            await SendStatusSnapshotAsync(responseWriter, cancellationToken);
            _logger.LogDebug("Initial status snapshot sent to subscriber {SubscriberId}", subscriberId);

            // Keep connection alive until client disconnects or cancellation
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SSE subscriber disconnected: {SubscriberId}", subscriberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE subscription {SubscriberId}: {ErrorMessage}", subscriberId, ex.Message);
        }
        finally
        {
            lock (_subscribersLock)
            {
                _subscribers.RemoveAll(s => s.Id == subscriberId);
            }
        }
    }

    /// <summary>
    /// Publishes a model status update to all connected clients.
    /// Called when model readiness, download state, or pause state changes.
    /// Non-blocking: publishes asynchronously to avoid stalling model operations.
    /// </summary>
    public void PublishModelStatusUpdate(string modelName, string status, int progressPercent, string? errorSummary = null)
    {
        _logger.LogDebug("Publishing model status update: model={Model}, status={Status}, progress={Progress}%", 
            modelName, status, progressPercent);

        _ = PublishEventAsync(new
        {
            type = "model_status_update",
            timestamp = DateTime.UtcNow,
            model = modelName,
            status = status,
            progressPercent = progressPercent,
            errorSummary = errorSummary
        });
    }

    /// <summary>
    /// Publishes an observability status update (traces, metrics, logs).
    /// </summary>
    public void PublishObservabilityStatusUpdate(string systemName, string state, string summary, List<string>? details = null)
    {
        _logger.LogDebug("Publishing observability status update: system={System}, state={State}", systemName, state);

        _ = PublishEventAsync(new
        {
            type = "observability_status_update",
            timestamp = DateTime.UtcNow,
            system = systemName,
            state = state,
            summary = summary,
            details = details ?? []
        });
    }

    /// <summary>
    /// Publishes a reconnect event with full status snapshot (after SSE connection drop/resume).
    /// </summary>
    public void PublishReconnectEvent()
    {
        _logger.LogDebug("Publishing reconnect event");

        _ = PublishEventAsync(new
        {
            type = "reconnect",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Publishes a warning state transition (e.g., degraded backup readiness).
    /// </summary>
    public void PublishWarningStatusUpdate(string section, string message)
    {
        _logger.LogDebug("Publishing warning status update: section={Section}, message={Message}", section, message);

        _ = PublishEventAsync(new
        {
            type = "warning_status_update",
            timestamp = DateTime.UtcNow,
            section = section,
            message = message
        });
    }

    /// <summary>
    /// Publishes an error state transition.
    /// </summary>
    public void PublishErrorStatusUpdate(string section, string message, string? errorDetails = null)
    {
        _logger.LogDebug("Publishing error status update: section={Section}, message={Message}", section, message);

        _ = PublishEventAsync(new
        {
            type = "error_status_update",
            timestamp = DateTime.UtcNow,
            section = section,
            message = message,
            errorDetails = errorDetails
        });
    }

    /// <summary>
    /// Sends the full health status snapshot to a client (used for initial subscription and reconnects).
    /// </summary>
    private async Task SendStatusSnapshotAsync(StreamWriter responseWriter, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("streamingdigest")
                ?? _configuration.GetConnectionString("postgres")
                ?? _configuration.GetConnectionString("Default")
                ?? string.Empty;

            var probes = await _compositeProbe.ProbeAllAsync(cancellationToken);
            var overallHealth = _compositeProbe.ComputeOverallHealth(probes);

            var health = new
            {
                type = "status_snapshot",
                timestamp = DateTime.UtcNow,
                overallHealth = overallHealth.ToString(),
                
                settings = await BuildSettingsSummaryAsync(connectionString, cancellationToken),
                models = await BuildModelsSummaryAsync(cancellationToken),
                observability = BuildObservabilitySummary(probes),
                storage = BuildStorageSummary(probes),
                backupReadiness = await BuildBackupReadinessSummaryAsync(cancellationToken)
            };

            await WriteEventAsync(responseWriter, "snapshot", JsonSerializer.Serialize(health), cancellationToken);
            _logger.LogDebug("Status snapshot sent: overallHealth={OverallHealth}", overallHealth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending status snapshot: {ErrorMessage}", ex.Message);
            await WriteEventAsync(responseWriter, "error", 
                JsonSerializer.Serialize(new { message = "Failed to load status snapshot", error = ex.Message }), 
                cancellationToken);
        }
    }

    private async Task<object> BuildSettingsSummaryAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return new { state = "Degraded", summary = "Connection string missing" };
            }

            var versionState = await _upgradeService.ReadVersionStateAsync(connectionString, cancellationToken);
            var state = (versionState.AppVersion is not null && versionState.DbSchemaVersion is not null) ? "Ready" : "Degraded";

            return new
            {
                state = state,
                summary = state == "Ready" ? $"Version {versionState.AppVersion} ready" : "Version verification pending"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building settings summary");
            return new { state = "Error", summary = "Settings check failed" };
        }
    }

    private async Task<object> BuildModelsSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var states = await _modelStateRepository.GetAllAsync(cancellationToken);
            var readyCount = states.Count(s => s.Status == "ready");
            var failedCount = states.Count(s => s.Status == "failed");
            
            var state = failedCount > 0 ? "Error" : readyCount > 0 ? "Ready" : "Degraded";

            return new
            {
                state = state,
                summary = $"{readyCount} ready, {failedCount} failed",
                count = states.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building models summary");
            return new { state = "Error", summary = "Models check failed", count = 0 };
        }
    }

    private object BuildObservabilitySummary(ServiceHealthDetails[] probes)
    {
        try
        {
            var obsProbe = probes.FirstOrDefault(p => p.ServiceName == "Observability");
            var state = obsProbe?.Status ?? DomainHealthState.Degraded;

            return new
            {
                state = state.ToString(),
                summary = state == DomainHealthState.Ready ? "Operational" : state.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building observability summary");
            return new { state = "Error", summary = "Observability check failed" };
        }
    }

    private object BuildStorageSummary(ServiceHealthDetails[] probes)
    {
        try
        {
            var postgresProbe = probes.FirstOrDefault(p => p.ServiceName == "Postgres");
            var state = postgresProbe?.Status ?? DomainHealthState.Error;

            return new
            {
                state = state.ToString(),
                summary = state == DomainHealthState.Ready ? "Operational" : state.ToString(),
                latencyMs = postgresProbe?.LatencyMs ?? (long?)null
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building storage summary");
            return new { state = "Error", summary = "Storage check failed", latencyMs = (long?)null };
        }
    }

    private async Task<object> BuildBackupReadinessSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _backupManifestChecker.GetBackupReadinessAsync(cancellationToken);
            return new
            {
                state = result.IsHealthy ? DomainHealthState.Ready : DomainHealthState.Degraded,
                summary = result.Status,
                lastBackupAt = result.LastBackupAtUtc
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building backup readiness summary");
            return new { state = DomainHealthState.Error, summary = "Backup check failed", lastBackupAt = (DateTime?)null };
        }
    }

    private async Task PublishEventAsync(object eventData)
    {
        if (_disposeCts?.IsCancellationRequested ?? true)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(eventData);
            
            List<PendingSubscriber> currentSubscribers;
            lock (_subscribersLock)
            {
                currentSubscribers = new List<PendingSubscriber>(_subscribers);
            }

            _logger.LogDebug("Publishing event to {SubscriberCount} subscribers", currentSubscribers.Count);

            var tasks = currentSubscribers
                .Select(sub => WriteEventAsync(sub.Writer, "update", json, sub.CancellationToken))
                .ToList();

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event: {ErrorMessage}", ex.Message);
        }
    }

    private static async Task WriteEventAsync(StreamWriter writer, string eventType, string data, CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteLineAsync($"event: {eventType}");
            await writer.WriteLineAsync($"data: {data}");
            await writer.WriteLineAsync();
            await writer.FlushAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Expected when client disconnects
        }
    }

    /// <summary>
    /// Starts background polling to detect and publish health state changes.
    /// Runs continuously until disposed, polling at 2-second intervals.
    /// Ensures status transitions reach clients without waiting for manual refresh (AC1).
    /// </summary>
    public void StartPolling()
    {
        if (_disposeCts?.Token.IsCancellationRequested == true)
        {
            _logger.LogDebug("Cannot start polling: service is disposed");
            return;
        }

        if (_pollingTask is not null)
        {
            _logger.LogDebug("Polling already started");
            return;
        }

        _pollingTask = Task.Run(() => PollHealthStatusAsync(_disposeCts?.Token ?? CancellationToken.None));
        _logger.LogInformation("Health status polling started (interval: 2s)");
    }

    private async Task PollHealthStatusAsync(CancellationToken cancellationToken)
    {
        const int pollIntervalMs = 2000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);

                await DetectAndPublishChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health status polling: {ErrorMessage}", ex.Message);
                // Continue polling even on error
            }
        }
    }

    private async Task DetectAndPublishChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("streamingdigest")
                ?? _configuration.GetConnectionString("postgres")
                ?? _configuration.GetConnectionString("Default")
                ?? string.Empty;

            var probes = await _compositeProbe.ProbeAllAsync(cancellationToken);
            var overallHealth = _compositeProbe.ComputeOverallHealth(probes);

            var currentState = new
            {
                overallHealth = overallHealth.ToString(),
                settings = await BuildSettingsSummaryAsync(connectionString, cancellationToken),
                models = await BuildModelsSummaryAsync(cancellationToken),
                observability = BuildObservabilitySummary(probes),
                storage = BuildStorageSummary(probes),
                backupReadiness = await BuildBackupReadinessSummaryAsync(cancellationToken)
            };

            // Only emit if state actually changed
            if (!StateEqual(_lastKnownState, currentState))
            {
                _logger.LogDebug("Health state change detected, publishing events");
                
                if (_lastKnownState is not null)
                {
                    _logger.LogInformation("Overall health state changed: {Current}", currentState.overallHealth);
                }

                _lastKnownState = currentState;

                // Emit granular update events so clients can react to specific changes
                await PublishChangeDetectedAsync("models", currentState.models, cancellationToken);
                await PublishChangeDetectedAsync("observability", currentState.observability, cancellationToken);
                await PublishChangeDetectedAsync("backupReadiness", currentState.backupReadiness, cancellationToken);
                await PublishChangeDetectedAsync("storage", currentState.storage, cancellationToken);
                await PublishChangeDetectedAsync("settings", currentState.settings, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting health state changes: {ErrorMessage}", ex.Message);
        }
    }

    private async Task PublishChangeDetectedAsync(string section, object? value, CancellationToken cancellationToken)
    {
        if (value is null)
            return;

        _ = PublishEventAsync(new
        {
            type = "state_update",
            timestamp = DateTime.UtcNow,
            section = section,
            data = value
        });
    }

    private static bool StateEqual(object? state1, object? state2)
    {
        if (state1 is null || state2 is null)
            return state1 == state2;

        var json1 = JsonSerializer.Serialize(state1);
        var json2 = JsonSerializer.Serialize(state2);
        return json1 == json2;
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts?.Cancel();
        _disposeCts?.Dispose();

        lock (_subscribersLock)
        {
            _subscribers.Clear();
        }

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        await Task.CompletedTask;
    }
}
