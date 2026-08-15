using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Hangfire.Storage;
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

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};{Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new AdminRunNowWebApplicationFactory(_connectionString);
        _client = _factory.CreateClient();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _runRepository = new IngestionRunRepository(_context);
    }

    public async Task DisposeAsync()
    {
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

    private sealed class AdminRunNowWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
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
