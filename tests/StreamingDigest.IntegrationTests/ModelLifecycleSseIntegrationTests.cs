using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration coverage for WS-6: <c>GET /api/models/events</c> (SSE) and
/// <c>GET /api/models/status</c>. Runs against the real API host with a containerized
/// PostgreSQL so auth middleware and schema behave exactly as in production. The
/// model-runtime-state repository is replaced with an in-memory double so the test can
/// drive a simulated lifecycle deterministically without waiting on Ollama pulls.
/// </summary>
[Collection("ModelLifecycleSse")]
public sealed class ModelLifecycleSseIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _containerName = $"streaming-digest-sse-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private ModelLifecycleWebApplicationFactory? _factory;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new ModelLifecycleWebApplicationFactory(_connectionString);
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

    [Fact]
    public async Task Status_endpoint_requires_authentication()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/models/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Events_endpoint_requires_authentication()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/models/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_endpoint_returns_seeded_model_runtime_state_snapshot()
    {
        using var client = CreateClient();

        var state = CreateState("bge-m3", "embedding", "ready", progressPercent: 100, lastVerifiedAt: DateTimeOffset.UtcNow);
        _factory!.StateRepository.Seed(state);

        await LoginAsync(client, "status-passw0rd-123!");

        var response = await client.GetAsync("/api/models/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var model = Assert.Single(payload.RootElement.GetProperty("models").EnumerateArray());
        Assert.Equal("ollama", model.GetProperty("provider").GetString());
        Assert.Equal("bge-m3", model.GetProperty("modelId").GetString());
        Assert.Equal("embedding", model.GetProperty("runtimeRole").GetString());
        Assert.Equal("ready", model.GetProperty("status").GetString());
        Assert.Equal(100, model.GetProperty("progressPercent").GetInt32());
    }

    [Fact]
    public async Task Events_endpoint_streams_expected_sequence_for_a_simulated_lifecycle()
    {
        using var client = CreateClient(longLivedRequests: true);

        await LoginAsync(client, "events-passw0rd-123!");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/models/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync();
        var readTask = ReadEventsAsync(stream, expectedEventCount: 4);

        // Wait for the server-side subscription before publishing; the broadcaster has no
        // replay, so events sent before the subscription registers would be lost.
        await WaitForServerSubscriptionAsync();

        // Drive a simulated lifecycle through persisted state changes, publishing one
        // broadcaster event per transition exactly as the download job / verify endpoint will.
        var operationId = Guid.NewGuid();
        var transitions = new (string Status, int? Progress, string? Error)[]
        {
            ("queued", null, null),
            ("running", 0, null),
            ("running", 55, null),
            ("failed", null, "pull stalled")
        };

        foreach (var (status, progress, error) in transitions)
        {
            var state = CreateState("qwen2.5:7b", "llm", status, operationId, progress, error);
            _factory!.StateRepository.Seed(state);
            _factory!.Broadcaster.Publish(ModelRuntimeStateEvents.FromStateTransition(state));
        }

        var events = await readTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(
            new[] { "model.status", "model.status", "model.status", "operation.failed" },
            events.Select(e => e.Name));

        var queued = JsonDocument.Parse(events[0].Data);
        Assert.Equal("queued", queued.RootElement.GetProperty("status").GetString());
        Assert.Equal("qwen2.5:7b", queued.RootElement.GetProperty("modelId").GetString());

        var runningProgress = JsonDocument.Parse(events[2].Data);
        Assert.Equal("running", runningProgress.RootElement.GetProperty("status").GetString());
        Assert.Equal(55, runningProgress.RootElement.GetProperty("progressPercent").GetInt32());

        var failed = JsonDocument.Parse(events[3].Data);
        Assert.Equal(operationId, failed.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("pull stalled", failed.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Events_endpoint_does_not_replay_events_published_before_the_subscription()
    {
        using var client = CreateClient(longLivedRequests: true);

        // Publish before anyone subscribes: the SSE contract reconciles via the status
        // snapshot endpoint instead of replaying history.
        var stale = CreateState("bge-m3", "embedding", "queued");
        _factory!.Broadcaster.Publish(ModelRuntimeStateEvents.FromStateTransition(stale));

        await LoginAsync(client, "noreplay-passw0rd-123!");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/models/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var stream = await response.Content.ReadAsStreamAsync();
        var readTask = ReadEventsAsync(stream, expectedEventCount: 1);

        // Wait for the server-side subscription before publishing the post-subscribe event.
        await WaitForServerSubscriptionAsync();

        var fresh = CreateState("llama3.1:8b", "llm", "ready", currentOperationId: Guid.NewGuid());
        _factory!.Broadcaster.Publish(ModelRuntimeStateEvents.FromStateTransition(fresh));

        var events = await readTask.WaitAsync(TimeSpan.FromSeconds(15));

        var single = Assert.Single(events);
        Assert.Equal("operation.completed", single.Name);
        Assert.DoesNotContain("queued", single.Data, StringComparison.Ordinal);
    }

    private static ModelRuntimeState CreateState(
        string modelId,
        string runtimeRole,
        string status,
        Guid? currentOperationId = null,
        int? progressPercent = null,
        string? lastErrorSummary = null,
        DateTimeOffset? lastVerifiedAt = null)
    {
        return new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = modelId,
            RuntimeRole = runtimeRole,
            Status = status,
            CurrentOperationId = currentOperationId,
            ProgressPercent = progressPercent,
            LastErrorSummary = lastErrorSummary,
            LastVerifiedAt = lastVerifiedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private HttpClient CreateClient(bool longLivedRequests = false)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        if (longLivedRequests)
        {
            // SSE streams stay open for the life of the connection; disable the default
            // 100-second HttpClient timeout so the read isn't aborted mid-stream.
            client.Timeout = Timeout.InfiniteTimeSpan;
        }

        return client;
    }

    private static async Task LoginAsync(HttpClient client, string newPassword)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Assert.True(loginResponse.StatusCode == HttpStatusCode.OK, $"Login failed with {loginResponse.StatusCode}: {loginBody}");

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>(JsonOptions);
        Assert.NotNull(me);

        if (!me!.MustChangePassword)
        {
            // A prior test in this shared container already rotated the bootstrap password.
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

    /// <summary>
    /// The broadcaster has no replay and no "subscribed" signal, so publishing immediately
    /// after <see cref="HttpCompletionOption.ResponseHeadersRead"/> can race the server-side
    /// subscription registration. Wait until the server actually holds a subscriber before
    /// driving the lifecycle, otherwise events can be lost.
    /// </summary>
    private async Task WaitForServerSubscriptionAsync()
    {
        var broadcaster = (ModelLifecycleEventBroadcaster)_factory!.Broadcaster;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (broadcaster.SubscriberCount == 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The SSE endpoint did not register a subscriber in time.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task<List<SseEvent>> ReadEventsAsync(Stream stream, int expectedEventCount)
    {
        var events = new List<SseEvent>();
        using var reader = new StreamReader(stream);

        string? currentEventName = null;
        while (events.Count < expectedEventCount)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && currentEventName is not null)
            {
                events.Add(new SseEvent(currentEventName, line["data: ".Length..]));
                currentEventName = null;
            }
        }

        return events;
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL SSE integration test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new InvalidOperationException("PostgreSQL did not become ready in time for the SSE integration tests.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }

    private sealed record SseEvent(string Name, string Data);

    private sealed record CsrfTokenResponse(string Token);

    private sealed record AuthMeResponse(string Username, bool MustChangePassword);

    private sealed class InMemoryModelRuntimeStateRepository : IModelRuntimeStateRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Provider, string ModelId), ModelRuntimeState> _states = [];

        public void Seed(ModelRuntimeState state)
        {
            lock (_gate)
            {
                _states[(state.Provider, state.ModelId)] = state;
            }
        }

        public Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
        {
            Seed(state);
            return Task.CompletedTask;
        }

        public Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _states.TryGetValue((provider, modelId), out var state);
                return Task.FromResult(state);
            }
        }

        public Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ModelRuntimeState>>(
                    _states.Values.Where(s => string.Equals(s.Provider, provider, StringComparison.Ordinal)).OrderBy(s => s.ModelId).ToList());
            }
        }

        public Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<ModelRuntimeState>>(
                    _states.Values.OrderBy(s => s.Provider).ThenBy(s => s.ModelId).ToList());
            }
        }
    }

    private sealed class ModelLifecycleWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public InMemoryModelRuntimeStateRepository StateRepository { get; } = new();

        public IModelLifecycleEventBroadcaster Broadcaster =>
            Services.GetRequiredService<IModelLifecycleEventBroadcaster>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:streamingdigest", connectionString);
            builder.UseSetting("ConnectionStrings:postgres", connectionString);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:streamingdigest"] = connectionString,
                    ["ConnectionStrings:postgres"] = connectionString,
                    ["BOOTSTRAP_ADMIN_USERNAME"] = BootstrapUsername,
                    ["BOOTSTRAP_ADMIN_PASSWORD"] = BootstrapPassword
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();

                services.RemoveAll<IModelRuntimeStateRepository>();
                services.AddSingleton<IModelRuntimeStateRepository>(StateRepository);
            });
        }
    }
}
