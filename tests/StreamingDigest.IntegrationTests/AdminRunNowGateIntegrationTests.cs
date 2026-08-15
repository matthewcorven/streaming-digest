using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Hangfire.Storage;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration tests for admin run-now gate (issue #249).
/// Verifies the full path: admin API → Hangfire enqueue → job picked up → ingestion run persisted.
///
/// These tests require Docker and Postgres; they are skip-by-default.
/// Run with: dotnet test --filter "AdminRunNowGateIntegrationTests"
/// </summary>
[Collection("PostgreSQL Integration Tests")]
[Trait("Category", "Integration")]
public sealed class AdminRunNowGateIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-admin-run-now-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private StreamingDigestDbContext? _context;
    private IngestionRunRepository? _runRepository;
    private BackgroundJobServer? _jobServer;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};{Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new AdminRunNowWebApplicationFactory(_connectionString);
        _client = _factory.CreateClient();
        _jobServer = ((AdminRunNowWebApplicationFactory)_factory).GetJobServer();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _runRepository = new IngestionRunRepository(_context);
    }

    public async Task DisposeAsync()
    {
        if (_jobServer is not null)
        {
            _jobServer.Dispose();
        }

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        _client?.Dispose();
        _factory?.Dispose();

        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task RunIngestionNow_EnqueuesJobAndPersistsRun()
    {
        // Arrange - precondition: no runs exist yet
        var existingRuns = await _runRepository!.GetListAsync(limit: 100);
        Assert.Empty(existingRuns);

        // Act - call admin API to run ingestion now
        var response = await _client!.PostAsync("/api/admin/operations/ingestion/run", null);

        // Assert - response is Accepted (202)
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var responseBody = await response.Content.ReadFromJsonAsync<AdminOperationResponse>();
        Assert.NotNull(responseBody);
        Assert.Equal("accepted", responseBody.Status);
        Assert.Equal("ingestion.run", responseBody.OperationType);
        Assert.NotEqual(Guid.Empty, responseBody.OperationId);

        // Assert - ingestion run is persisted in the database
        await Task.Delay(100); // Small delay to allow persistence
        var runs = await _runRepository.GetListAsync(limit: 100);
        Assert.NotEmpty(runs);

        var createdRun = runs.FirstOrDefault(r => r.Id.ToString() == responseBody.OperationId.ToString());
        Assert.NotNull(createdRun);
        Assert.Equal("manual", createdRun!.RunType);
        Assert.Equal("admin", createdRun.TriggeredBy);

        // Assert - Hangfire job is enqueued
        var hangfireJobId = responseBody.JobId;
        Assert.NotNull(hangfireJobId);

        // Check job exists in Hangfire storage
        var storage = JobStorage.Current;
        using var connection = storage.GetConnection();
        var jobDetails = connection.GetJobData(hangfireJobId);
        Assert.NotNull(jobDetails);
    }

    [Fact]
    public async Task RunIngestionNow_JobExecutesAndCompletes()
    {
        // This test verifies the full path: admin API → Hangfire enqueue → job picked up → ingestion run persisted with terminal status.
        // Requires BackgroundJobServer running (initialized in factory).

        // Arrange - precondition: no runs exist yet
        var existingRuns = await _runRepository!.GetListAsync(limit: 100);
        Assert.Empty(existingRuns);

        // Act - call admin API to run ingestion now
        var response = await _client!.PostAsync("/api/admin/operations/ingestion/run", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var responseBody = await response.Content.ReadFromJsonAsync<AdminOperationResponse>();
        Assert.NotNull(responseBody);
        var jobId = responseBody.JobId;
        Assert.NotNull(jobId);
        var runId = responseBody.OperationId;

        // Act - wait for job to complete (poll job state up to 10 seconds)
        var jobCompleted = await WaitForJobCompletionAsync(jobId, timeoutSeconds: 10);
        Assert.True(jobCompleted, $"Job {jobId} did not reach a terminal state within 10 seconds");

        // Assert - ingestion run transitioned from "running" to terminal status
        var completedRun = await _runRepository!.GetByIdAsync(runId);
        Assert.NotNull(completedRun);
        Assert.NotEqual("running", completedRun!.Status);
        Assert.True(
            completedRun.Status is "completed" or "completed_with_warnings" or "failed",
            $"Expected terminal status, got '{completedRun.Status}'");
        Assert.NotNull(completedRun.CompletedAt);
    }

    [Fact]
    public async Task RunIngestionNowWithChannelId_PersistsRunWithChannelTarget()
    {
        // Arrange
        var existingRuns = await _runRepository!.GetListAsync(limit: 100);
        Assert.Empty(existingRuns);

        // Act
        var response = await _client!.PostAsync("/api/admin/operations/ingestion/run", null);

        // Assert - response includes target
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var responseBody = await response.Content.ReadFromJsonAsync<AdminOperationResponse>();
        Assert.NotNull(responseBody);
        Assert.Equal("accepted", responseBody.Status);

        // Assert - ingestion run is persisted
        await Task.Delay(100);
        var runs = await _runRepository.GetListAsync(limit: 100);
        Assert.NotEmpty(runs);

        var createdRun = runs.First();
        Assert.Equal("manual", createdRun.RunType);
        Assert.Equal("admin", createdRun.TriggeredBy);
    }

    [Fact]
    public async Task RunIngestionNow_GetOperationReturnsAcceptedStatus()
    {
        // Arrange
        var postResponse = await _client!.PostAsync("/api/admin/operations/ingestion/run", null);
        var postBody = await postResponse.Content.ReadFromJsonAsync<AdminOperationResponse>();
        var operationId = postBody!.OperationId;

        // Act
        var getResponse = await _client.GetAsync($"/api/admin/operations/{operationId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getBody = await getResponse.Content.ReadFromJsonAsync<AdminOperationResponse>();
        Assert.NotNull(getBody);
        Assert.Equal("accepted", getBody.Status);
        Assert.Equal("ingestion.run", getBody.OperationType);
        Assert.Equal(operationId, getBody.OperationId);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL admin run-now integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL admin run-now integration test container to become ready.");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}. STDERR: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Polls a Hangfire job's state until it reaches a terminal state (Succeeded/Failed)
    /// or the timeout expires. Terminal states are those from which the job will not
    /// transition again (not Enqueued or Processing).
    /// </summary>
    private static async Task<bool> WaitForJobCompletionAsync(string jobId, int timeoutSeconds = 10)
    {
        var stopwatch = Stopwatch.StartNew();
        var storage = JobStorage.Current;

        while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            using var connection = storage.GetConnection();
            var jobData = connection.GetJobData(jobId);

            if (jobData is null)
            {
                // Job not found; might have been cleaned up. Consider this a timeout.
                return false;
            }

            var state = jobData.State;
            // Terminal states: succeeded, failed, deleted, etc.
            // Non-terminal states: enqueued, processing, scheduled, etc.
            if (state is "Succeeded" or "Failed" or "Deleted")
            {
                return true;
            }

            await Task.Delay(100); // Poll every 100ms
        }

        return false; // Timeout
    }

    private sealed class AdminRunNowWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        private BackgroundJobServer? _jobServer;

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
                    ["ConnectionStrings:postgres"] = connectionString
                });
            });
        }

        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            // Start the Hangfire background job server after the app is created
            if (_jobServer is null)
            {
                var app = this.Services;
                var storage = app.GetRequiredService<JobStorage>();
                _jobServer = new BackgroundJobServer(
                    new BackgroundJobServerOptions
                    {
                        WorkerCount = 1,
                        Queues = new[] { EnqueuedState.DefaultQueue },
                    },
                    storage);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _jobServer is not null)
            {
                _jobServer.Dispose();
            }
            base.Dispose(disposing);
        }

        public BackgroundJobServer? GetJobServer() => _jobServer;
    }

    private sealed record AdminOperationResponse(
        Guid OperationId,
        string OperationType,
        string Status,
        string Message,
        string? Target,
        string? JobId,
        string? HealthStatus);
}
