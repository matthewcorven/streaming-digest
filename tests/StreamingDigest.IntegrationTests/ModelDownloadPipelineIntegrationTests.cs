extern alias StreamingDigestWorker;

using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigestWorker::StreamingDigest.Worker.ModelDownload;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// WS-5 integration coverage: run the real worker download pipeline against a real Postgres
/// container with a stubbed Ollama runtime, asserting model_runtime_state + operations
/// transitions are actually persisted.
/// </summary>
public sealed class ModelDownloadPipelineIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-modeldl-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        await RunProcessAsync("docker",
        [
            "run", "--rm", "-d", "--name", _containerName,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        ]);

        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        var schemaGuard = new ModelRuntimeStateSchemaGuard();
        await schemaGuard.EnsureSchemaAsync(_connectionString);
    }

    public async Task DisposeAsync()
    {
        try
        {
            await RunProcessAsync("docker", ["rm", "-f", _containerName]);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public async Task WorkerPipeline_PersistsQueuedRunningReadyTransitions()
    {
        var operationId = Guid.NewGuid();
        var operations = new PostgresOperationStore(_connectionString);
        var now = DateTimeOffset.UtcNow;
        await operations.PersistAsync(new OperationRecord
        {
            Id = operationId,
            OperationType = "model.download",
            Status = "queued",
            CreatedAt = now,
            UpdatedAt = now
        });

        var stateRepository = new PostgresModelRuntimeStateRepository(_connectionString);
        await stateRepository.UpsertAsync(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "queued",
            CurrentOperationId = operationId,
            UpdatedAt = now
        });

        var runtime = new StubRuntimeClient
        {
            Progress =
            [
                new ModelPullProgress("downloading", 100, 40, 40),
                new ModelPullProgress("downloading", 100, 100, 100),
                new ModelPullProgress("success", null, null, null)
            ],
            Installed = [new ModelPresence("ollama", "bge-m3:latest", "sha256:real", 42)]
        };

        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime);
        var command = new ModelDownloadCommand(operationId, "ollama", "bge-m3", "embedding", now);

        await service.StartAsync(CancellationToken.None);
        Assert.True(queue.TryEnqueue(command));
        await WaitForAsync(async () =>
        {
            var state = await stateRepository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
            return state?.Status == "ready";
        });
        await service.StopAsync(CancellationToken.None);

        var finalState = await stateRepository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
        Assert.NotNull(finalState);
        Assert.Equal("ready", finalState!.Status);
        Assert.Equal(100, finalState.ProgressPercent);
        Assert.Equal(operationId, finalState.CurrentOperationId);

        var operation = await operations.GetByIdAsync(operationId);
        Assert.NotNull(operation);
        Assert.Equal("completed", operation!.Status);
        Assert.NotNull(operation.StartedAt);
        Assert.NotNull(operation.CompletedAt);
    }

    [Fact]
    public async Task WorkerPipeline_PullFailure_PersistsFailedState()
    {
        var operationId = Guid.NewGuid();
        var operations = new PostgresOperationStore(_connectionString);
        var now = DateTimeOffset.UtcNow;
        await operations.PersistAsync(new OperationRecord
        {
            Id = operationId,
            OperationType = "model.download",
            Status = "queued",
            CreatedAt = now,
            UpdatedAt = now
        });

        var stateRepository = new PostgresModelRuntimeStateRepository(_connectionString);
        var runtime = new StubRuntimeClient { PullException = new InvalidOperationException("connection refused") };
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new ModelDownloadCommand(operationId, "ollama", "bge-m3", "embedding", now));
        await WaitForAsync(async () =>
        {
            var state = await stateRepository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
            return state?.Status == "failed";
        });
        // Grace window: WaitForAsync can observe the state-row write while the rest of the
        // terminal transition (operations row, notification) is still in flight. Let the
        // pipeline settle before stopping the host so StopAsync cannot race the write.
        await Task.Delay(250);
        await service.StopAsync(CancellationToken.None);

        var finalState = await stateRepository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
        Assert.NotNull(finalState);
        Assert.Equal("failed", finalState!.Status);
        Assert.Contains("connection refused", finalState.LastErrorSummary);

        var operation = await operations.GetByIdAsync(operationId);
        Assert.Equal("failed", operation!.Status);
        Assert.Contains("connection refused", operation.ErrorSummary);
    }

    private ModelDownloadHostedService CreateService(ChannelModelDownloadQueue queue, StubRuntimeClient runtime)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ModelDownloadHostedService(
            queue,
            runtime,
            new PostgresModelRuntimeStateRepository(_connectionString),
            new PostgresOperationStore(_connectionString),
            new AppReadinessStateService(),
            _connectionString,
            services,
            NullLogger<ModelDownloadHostedService>.Instance);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(50);
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the model download integration test PostgreSQL container.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }

    private sealed class StubRuntimeClient : IModelRuntimeClient
    {
        public List<ModelPullProgress> Progress { get; set; } = [];
        public List<ModelPresence> Installed { get; set; } = [];
        public Exception? PullException { get; set; }

        public async IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (PullException is not null)
            {
                throw PullException;
            }

            foreach (var item in Progress)
            {
                yield return item;
                await Task.Yield();
            }
        }

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelPresence>>(Installed);

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelRuntimeInfo("ollama", model, null, []));
    }
}
