using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Application.Models;

/// <summary>
/// Bridges PostgreSQL LISTEN/NOTIFY for model state changes into <see cref="IModelLifecycleEventBroadcaster"/>.
/// Runs as a background service, maintains a dedicated connection to PostgreSQL, and listens on the
/// "model_state_changed" channel. When a notification arrives, deserializes it to a ModelLifecycleEvent
/// and publishes to all SSE subscribers. This allows cross-process signal propagation: worker updates
/// model_runtime_state in the database, the NOTIFY fires, the API listens and broadcasts to all clients.
/// </summary>
public sealed class PostgresModelLifecycleEventListener : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly IModelLifecycleEventBroadcaster _broadcaster;
    private readonly ILogger<PostgresModelLifecycleEventListener> _logger;
    private NpgsqlConnection? _connection;
    private Task? _listenerTask;
    private CancellationTokenSource? _cts;

    public PostgresModelLifecycleEventListener(
        string connectionString,
        IModelLifecycleEventBroadcaster broadcaster,
        ILogger<PostgresModelLifecycleEventListener> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the listener background task. Safe to call multiple times; a second call is a no-op.
    /// </summary>
    public void StartListening()
    {
        if (_cts is not null)
        {
            return; // Already started
        }

        _cts = new CancellationTokenSource();
        _listenerTask = RunListenerLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Gracefully stops the listener and cleans up the database connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_listenerTask is not null)
            {
                try
                {
                    await _listenerTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while stopping PostgreSQL listener");
                }
            }

            _cts.Dispose();
            _cts = null;
            _listenerTask = null;
        }

        if (_connection is not null)
        {
            try
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Notification -= HandleConnectionNotification;
                }
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing PostgreSQL listener connection");
            }

            _connection = null;
        }
    }

    private async Task RunListenerLoopAsync(CancellationToken cancellationToken)
    {
        const int maxReconnectDelayMs = 30_000;
        var reconnectDelayMs = 1_000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_connection is not null)
                {
                    await _connection.CloseAsync();
                }

                _connection = new NpgsqlConnection(_connectionString);
                await _connection.OpenAsync(cancellationToken);

                // Subscribe to notifications
                _connection.Notification += HandleConnectionNotification;

                // Subscribe to the model_state_changed channel
                using var command = _connection.CreateCommand();
                command.CommandText = "LISTEN model_state_changed;";
                await command.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("PostgreSQL listener connected and listening on channel: model_state_changed");
                reconnectDelayMs = 1_000;

                // Keep the connection alive - just wait for cancellation
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostgreSQL listener connection failed; will reconnect after {DelayMs}ms", reconnectDelayMs);

                try
                {
                    await Task.Delay(reconnectDelayMs, cancellationToken);
                    reconnectDelayMs = Math.Min(reconnectDelayMs * 2, maxReconnectDelayMs);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("PostgreSQL listener stopped");
    }

    private void HandleConnectionNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.Payload))
            {
                Debug.WriteLine($"[PostgresModelLifecycleEventListener] Received empty notification on channel {e.Channel}");
                return;
            }

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var modelEvent = JsonSerializer.Deserialize<ModelLifecycleEvent>(e.Payload, options);

            if (modelEvent is not null)
            {
                Debug.WriteLine($"[PostgresModelLifecycleEventListener] Publishing cross-process model event: {modelEvent.Name}");
                _broadcaster.Publish(modelEvent);
            }
            else
            {
                _logger.LogWarning("Failed to deserialize model event from notification: {Payload}", e.Payload);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in notification payload: {Payload}", e.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling notification");
        }
    }
}
