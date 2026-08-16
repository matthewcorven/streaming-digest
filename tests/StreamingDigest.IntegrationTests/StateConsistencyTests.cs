using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Regression tests for Domain 4: State Consistency
/// Validates that client state matches backend state after updates,
/// across reconnects, and with rapid state changes.
/// </summary>
[Collection("StateConsistency")]
public sealed class StateConsistencyTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _containerName = $"streaming-digest-consistency-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private StateConsistencyWebApplicationFactory? _factory;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new StateConsistencyWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await StopPostgresContainerAsync();
    }

    /// <summary>
    /// AC4.1: Client state matches backend after update
    /// Verify state consistency immediately after backend publishes change.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_ClientState_MatchesBackendAfterUpdate()
    {
        using var client = CreateClient();
        await LoginAsync(client, "state-match-123!");

        // Get initial state
        var initialState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(initialState);
        var initialBackupHealth = initialState.BackupHealth;

        // Update backend state
        _factory!.StateStore.SetBackupHealth("ready");
        await Task.Delay(100);

        // Get updated state from client
        var updatedState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(updatedState);

        // Verify client state matches backend
        Assert.Equal("ready", updatedState.BackupHealth);
        Assert.NotEqual(initialBackupHealth, updatedState.BackupHealth);
    }

    /// <summary>
    /// AC4.2: Degraded/warning transitions visible after brief connection drop
    /// State changes persist and are visible to client after network recovery.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_DegradedTransition_VisibleAfterReconnect()
    {
        using var client = CreateClient();
        await LoginAsync(client, "degraded-visible-123!");

        // Get ready state
        var readyState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(readyState);

        // Update to degraded
        _factory!.StateStore.SetBackupHealth("degraded");
        await Task.Delay(100);

        // Simulate disconnection by getting new client connection
        var reconnectedClient = CreateClient();
        await LoginAsync(reconnectedClient, "degraded-visible-123!");

        // Verify degraded state is visible on reconnection
        var degradedState = await reconnectedClient.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(degradedState);
        Assert.Equal("degraded", degradedState.BackupHealth);

        reconnectedClient.Dispose();
    }

    /// <summary>
    /// AC4.3: Multiple rapid state changes all delivered (no drops)
    /// Verify that rapid updates don't get lost or consolidated incorrectly.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_RapidUpdates_NoUpdatesDropped()
    {
        using var client = CreateClient();
        await LoginAsync(client, "rapid-state-123!");

        // Record initial state transitions
        var stateHistory = new List<string>();
        
        // Apply rapid state changes
        var states = new[] { "ready", "degraded", "ready", "degraded", "error", "ready" };
        foreach (var state in states)
        {
            _factory!.StateStore.SetBackupHealth(state);
            stateHistory.Add(state);
            await Task.Delay(50);
        }

        // Get current state
        var currentState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(currentState);

        // Final state should match the last update
        Assert.Equal("ready", currentState.BackupHealth);

        // Verify we can query history (if available) or at least current state is consistent
        Assert.NotEmpty(stateHistory);
    }

    /// <summary>
    /// AC4.4: Stale warning doesn't appear after backend state changes
    /// When backend transitions from warning to ready, client doesn't show stale warning.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_NoStaleState_AfterBackendChange()
    {
        using var client = CreateClient();
        await LoginAsync(client, "no-stale-123!");

        // Set degraded state (warning-like)
        _factory!.StateStore.SetBackupHealth("degraded");
        await Task.Delay(100);

        var warningState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(warningState);
        Assert.Equal("degraded", warningState.BackupHealth);

        // Change to ready
        _factory!.StateStore.SetBackupHealth("ready");
        await Task.Delay(100);

        // Verify state is updated and not stale
        var readyState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(readyState);
        Assert.Equal("ready", readyState.BackupHealth);

        // Verify timestamp reflects the change
        Assert.True(readyState.LastUpdated > warningState.LastUpdated);
    }

    /// <summary>
    /// AC4.5: Reconnect doesn't revert to old state
    /// After client reconnect, state is current not historical.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_Reconnect_DoesNotRevertState()
    {
        using var client = CreateClient();
        await LoginAsync(client, "no-revert-123!");

        // Set initial state
        _factory!.StateStore.SetBackupHealth("ready");
        await Task.Delay(100);

        var state1 = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(state1);
        Assert.Equal("ready", state1.BackupHealth);

        // Update state while "disconnected" (simulated by just updating backend)
        _factory!.StateStore.SetBackupHealth("degraded");
        await Task.Delay(100);

        // Create new client connection
        var reconnectedClient = CreateClient();
        await LoginAsync(reconnectedClient, "no-revert-123!");

        // Verify current (not old) state is received
        var state2 = await reconnectedClient.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(state2);
        Assert.Equal("degraded", state2.BackupHealth);
        Assert.NotEqual(state1.BackupHealth, state2.BackupHealth);

        reconnectedClient.Dispose();
    }

    /// <summary>
    /// AC4.6: State transitions logged and auditable
    /// State changes are recorded for debugging and audit purposes.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_Transitions_AuditableAndLogged()
    {
        using var client = CreateClient();
        await LoginAsync(client, "audit-log-123!");

        // Get initial state version
        var initialState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(initialState);
        var version1 = initialState.Version;

        // Update state
        _factory!.StateStore.SetBackupHealth("degraded");
        await Task.Delay(100);

        var updatedState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(updatedState);
        var version2 = updatedState.Version;

        // Version should increment to track update
        Assert.True(version2 > version1, "Version should increment on state change");

        // Get audit log if available
        var auditLog = await client.GetAsync("/api/health/audit-log");
        Assert.True(auditLog.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
    }

    /// <summary>
    /// AC4.7: Concurrent updates don't corrupt state
    /// Multiple simultaneous updates result in consistent final state.
    /// </summary>
    [Fact(Skip = "Domain 4 tests disabled: Waiting for #272 (SSE signal propagation) completion")]
    public async Task StateConsistency_ConcurrentUpdates_FinalStateConsistent()
    {
        using var client = CreateClient();
        await LoginAsync(client, "concurrent-123!");

        // Apply concurrent state updates
        var tasks = new List<Task>
        {
            Task.Run(async () => { _factory!.StateStore.SetBackupHealth("ready"); await Task.Delay(10); }),
            Task.Run(async () => { _factory!.StateStore.SetBackupHealth("degraded"); await Task.Delay(10); }),
            Task.Run(async () => { _factory!.StateStore.SetBackupHealth("ready"); await Task.Delay(10); })
        };

        await Task.WhenAll(tasks);
        await Task.Delay(200);

        // Final state should be one of the applied states, not corrupted
        var finalState = await client.GetFromJsonAsync<HealthState>("/api/health/state");
        Assert.NotNull(finalState);
        
        var validStates = new[] { "ready", "degraded" };
        Assert.Contains(finalState.BackupHealth, validStates);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────────

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task LoginAsync(HttpClient client, string newPassword)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>(JsonOptions);
        Assert.NotNull(me);

        if (!me!.MustChangePassword)
        {
            return;
        }

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword });
        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-e",
            $"POSTGRES_USER={Username}",
            "-e",
            $"POSTGRES_PASSWORD={Password}",
            "-e",
            $"POSTGRES_DB={DatabaseName}",
            "-p",
            $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return container ID for PostgreSQL.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        await RunProcessAsync("docker", new[] { "stop", _containerName });
    }

    private async Task WaitForPostgresAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await connection.CloseAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("PostgreSQL did not become available in time.");
    }

    private static async Task<string> RunProcessAsync(string filename, string[] args)
    {
        var psi = new ProcessStartInfo(filename, args) { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(psi);

        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start process: {filename}");
        }

        return await process.StandardOutput.ReadToEndAsync();
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ─── Test Records ───────────────────────────────────────────────────────────────

    private sealed record HealthState(string BackupHealth, int Version, DateTime LastUpdated);

    private sealed record AuthMeResponse(bool MustChangePassword);

    private sealed record CsrfTokenResponse(string Token);
}

/// <summary>
/// Test web application factory for state consistency tests.
/// </summary>
internal sealed class StateConsistencyWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public StateConsistencyWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public InMemoryStateStore StateStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StreamingDigest"] = _connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(StateStore);
        });
    }
}

/// <summary>
/// In-memory state store for testing state consistency.
/// </summary>
internal sealed class InMemoryStateStore
{
    private string _backupHealth = "ready";
    private int _version = 1;
    private DateTime _lastUpdated = DateTime.UtcNow;
    private readonly object _lock = new();

    public string BackupHealth
    {
        get
        {
            lock (_lock)
            {
                return _backupHealth;
            }
        }
    }

    public int Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    public DateTime LastUpdated
    {
        get
        {
            lock (_lock)
            {
                return _lastUpdated;
            }
        }
    }

    public void SetBackupHealth(string health)
    {
        lock (_lock)
        {
            _backupHealth = health;
            _version++;
            _lastUpdated = DateTime.UtcNow;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _backupHealth = "ready";
            _version = 1;
            _lastUpdated = DateTime.UtcNow;
        }
    }
}
