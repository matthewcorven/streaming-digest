using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Worker.ModelDownload;

namespace StreamingDigest.UnitTests;

public sealed class ChannelModelDownloadQueueTests
{
    private static ModelDownloadCommand Command(string modelId = "bge-m3", string provider = "ollama", Guid? operationId = null)
        => new(operationId ?? Guid.NewGuid(), provider, modelId, "embedding", DateTimeOffset.UtcNow);

    [Fact]
    public void TryEnqueue_DeduplicatesSameModel()
    {
        var queue = new ChannelModelDownloadQueue();

        Assert.True(queue.TryEnqueue(Command()));
        Assert.False(queue.TryEnqueue(Command())); // same provider+model, different operation
    }

    [Fact]
    public void TryEnqueue_AllowsDifferentModels()
    {
        var queue = new ChannelModelDownloadQueue();

        Assert.True(queue.TryEnqueue(Command("bge-m3")));
        Assert.True(queue.TryEnqueue(Command("llama3.1:8b")));
    }

    [Fact]
    public void Complete_ReleasesDedupSlot()
    {
        var queue = new ChannelModelDownloadQueue();
        var command = Command();

        Assert.True(queue.TryEnqueue(command));
        queue.Complete(command);
        Assert.True(queue.TryEnqueue(Command()));
    }

    [Fact]
    public async Task ReadAllAsync_DrainsEnqueuedCommands()
    {
        var queue = new ChannelModelDownloadQueue();
        var first = Command("bge-m3");
        var second = Command("llama3.1:8b");
        queue.TryEnqueue(first);
        queue.TryEnqueue(second);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<ModelDownloadCommand>();
        await foreach (var command in queue.ReadAllAsync(cts.Token))
        {
            read.Add(command);
            if (read.Count == 2)
            {
                break;
            }
        }

        Assert.Equal([first, second], read);
    }

    [Fact]
    public async Task TryEnqueue_FullChannel_ReportsDropDistinctFromDedup()
    {
        var queue = new ChannelModelDownloadQueue();

        // Occupy the bounded channel (capacity 32) with distinct models; no reader drains.
        for (var i = 0; i < 32; i++)
        {
            Assert.True(queue.TryEnqueue(Command($"model-{i}"), out var dropped));
            Assert.False(dropped);
        }

        // BoundedChannelFullMode.DropWrite may either reject the new write (TryWrite=false)
        // or silently accept it while dropping the pending item; assert both observability
        // guarantees that actually matter to the caller:
        //  1. A full-channel rejection is reported distinctly from a true dedup.
        //  2. A repeat of a *queued* model is always a true dedup (never a drop report).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var overflowEnqueued = queue.TryEnqueue(Command("model-overflow"), out var droppedBecauseFull);
        if (overflowEnqueued)
        {
            // Silently accepted (an earlier pending item was dropped instead): the dedup
            // slot for model-overflow is held, so a repeat must dedup, not re-enqueue.
            Assert.False(droppedBecauseFull);
            Assert.False(queue.TryEnqueue(Command("model-overflow"), out var repeatDrop));
            Assert.False(repeatDrop);
        }
        else
        {
            Assert.True(droppedBecauseFull);

            // A full-channel drop releases the dedup slot, so the same model can retry…
            Assert.False(queue.TryEnqueue(Command("model-0"), out var dedupDrop));
            Assert.False(dedupDrop);
        }

        // …and in both cases a queued model is still deduplicated by identity.
        var read = new List<ModelDownloadCommand>();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            read.Add(item);
            if (read.Count == 32)
            {
                break;
            }
        }

        Assert.Equal(32, read.Select(c => c.ModelId).Distinct().Count());
    }
}

public sealed class ModelDownloadJobTests
{
    [Fact]
    public void RunAsync_DisablesAutomaticRetries()
    {
        // The API persists the queued operation before enqueueing; Hangfire retries would
        // re-push commands for operations the pipeline may already have completed/failed.
        var attribute = typeof(ModelDownloadJob)
            .GetMethod(nameof(ModelDownloadJob.RunAsync))!
            .GetCustomAttributes(typeof(Hangfire.AutomaticRetryAttribute), inherit: true)
            .Cast<Hangfire.AutomaticRetryAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(0, attribute!.Attempts);
    }

    [Fact]
    public async Task RunAsync_EnqueuesIntoBoundedChannel()
    {
        var queue = new ChannelModelDownloadQueue();
        var job = new ModelDownloadJob(queue, NullLogger<ModelDownloadJob>.Instance);
        var command = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);

        await job.RunAsync(command);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<ModelDownloadCommand>();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            read.Add(item);
            break;
        }

        Assert.Single(read);
        Assert.Equal(command.OperationId, read[0].OperationId);
    }

    [Fact]
    public async Task RunAsync_DuplicateModelIsNoOp()
    {
        var queue = new ChannelModelDownloadQueue();
        var job = new ModelDownloadJob(queue, NullLogger<ModelDownloadJob>.Instance);

        await job.RunAsync(new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow));
        await job.RunAsync(new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = new List<ModelDownloadCommand>();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            read.Add(item);
            if (read.Count == 1)
            {
                break;
            }
        }

        Assert.Single(read);
    }
}

public sealed class ModelDownloadHostedServiceTests
{
    [Fact]
    public async Task Download_TransitionsQueuedToRunningToReady()
    {
        var runtime = new StubRuntimeClient
        {
            Progress =
            [
                new ModelPullProgress("pulling manifest", null, null, null),
                new ModelPullProgress("downloading", 100, 25, 25),
                new ModelPullProgress("downloading", 100, 100, 100),
                new ModelPullProgress("success", null, null, null)
            ],
            Installed = [new ModelPresence("ollama", "bge-m3:latest", "sha256:abc", 1234)]
        };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime, state, operations);
        var command = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);
        operations.Seed(new OperationRecord { Id = command.OperationId, OperationType = "model.download", Status = "queued", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(command);
        await WaitForAsync(() => state.Current?.Status == "ready");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("ready", state.Current?.Status);
        Assert.Equal(100, state.Current?.ProgressPercent);
        Assert.Equal(command.OperationId, state.Current?.CurrentOperationId);
        Assert.Contains(state.History, s => s == "running");

        var operation = operations.Get(command.OperationId);
        Assert.Equal("completed", operation?.Status);
        Assert.NotNull(operation?.CompletedAt);
        Assert.NotNull(operation?.StartedAt);
    }

    [Fact]
    public async Task Download_PullFailure_MarksFailedAndNotifies()
    {
        var runtime = new StubRuntimeClient { PullException = new InvalidOperationException("registry unreachable") };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime, state, operations);
        var command = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(command);
        await WaitForAsync(() => state.Current?.Status == "failed");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("failed", state.Current?.Status);
        Assert.Contains("registry unreachable", state.Current?.LastErrorSummary);

        var operation = operations.Get(command.OperationId);
        Assert.Equal("failed", operation?.Status);
        Assert.NotNull(operation?.CompletedAt);
        Assert.Contains("registry unreachable", operation?.ErrorSummary);
    }

    [Fact]
    public async Task Download_MissingPresenceAfterPull_MarksFailed()
    {
        var runtime = new StubRuntimeClient
        {
            Progress = [new ModelPullProgress("success", null, null, null)],
            Installed = [] // pull "succeeded" but /api/tags does not list the model
        };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime, state, operations);
        var command = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(command);
        await WaitForAsync(() => state.Current?.Status == "failed");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("failed", state.Current?.Status);
        Assert.Contains("did not appear", state.Current?.LastErrorSummary);
        Assert.Equal("failed", operations.Get(command.OperationId)?.Status);
    }

    [Fact]
    public async Task Download_DedupWhileRunning_SkipsSecondCommand()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StubRuntimeClient
        {
            ProgressGate = gate,
            Progress = [new ModelPullProgress("downloading", 100, 10, 10)],
            Installed = [new ModelPresence("ollama", "bge-m3", "sha256:abc", 10)]
        };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime, state, operations);
        var first = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);
        var second = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow);

        await service.StartAsync(CancellationToken.None);
        Assert.True(queue.TryEnqueue(first));
        await WaitForAsync(() => runtime.PullStarted);

        // While the first pull is still streaming, a duplicate must dedup.
        Assert.False(queue.TryEnqueue(second));

        gate.SetResult();
        await WaitForAsync(() => state.Current?.Status == "ready");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, runtime.PullCallCount);
    }

    [Fact]
    public async Task Download_ConcurrencyIsOne_SecondModelRunsAfterFirst()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StubRuntimeClient
        {
            Progress = [new ModelPullProgress("success", null, null, null)],
            Installed = [new ModelPresence("ollama", "bge-m3", "d1", 1), new ModelPresence("ollama", "llama3.1:8b", "d2", 2)],
            OnPull = model =>
            {
                if (model == "bge-m3")
                {
                    firstStarted.TrySetResult();
                    return releaseFirst.Task;
                }

                return Task.CompletedTask;
            }
        };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(queue, runtime, state, operations);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new ModelDownloadCommand(Guid.NewGuid(), "ollama", "bge-m3", "embedding", DateTimeOffset.UtcNow));
        queue.TryEnqueue(new ModelDownloadCommand(Guid.NewGuid(), "ollama", "llama3.1:8b", "llm", DateTimeOffset.UtcNow));

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, runtime.PullCallCount);

        releaseFirst.SetResult();
        await WaitForAsync(() => runtime.PullCallCount == 2);
        await WaitForAsync(() => state.StatusFor("llama3.1:8b") == "ready");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("ready", state.StatusFor("bge-m3"));
        Assert.Equal("ready", state.StatusFor("llama3.1:8b"));
    }

    [Fact]
    public async Task Download_StalledProgress_MarksFailed()
    {
        var runtime = new StubRuntimeClient
        {
            PullException = new TimeoutException("Model pull stalled after no progress for 50 seconds.")
        };
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        using var service = CreateService(
            queue,
            runtime,
            state,
            operations,
            startupRecoveryThreshold: TimeSpan.FromMinutes(1));
        var command = new ModelDownloadCommand(Guid.NewGuid(), "ollama", "qwen2.5:7b", "llm", DateTimeOffset.UtcNow);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(command);
        await WaitForAsync(() => state.Current?.Status == "failed");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("failed", state.Current?.Status);
        Assert.Contains("stalled", state.Current?.LastErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("failed", operations.Get(command.OperationId)?.Status);
    }

    [Fact]
    public async Task StartupRecovery_StaleRunningDownload_MarksFailed()
    {
        var runtime = new StubRuntimeClient();
        var state = new InMemoryStateRepository();
        var operations = new InMemoryOperationStore();
        var queue = new ChannelModelDownloadQueue();
        var operationId = Guid.NewGuid();
        var staleAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        state.Seed(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "qwen2.5:7b",
            RuntimeRole = "llm",
            Status = "running",
            CurrentOperationId = operationId,
            ProgressPercent = 21,
            UpdatedAt = staleAt
        });
        operations.Seed(new OperationRecord
        {
            Id = operationId,
            OperationType = "model.download",
            Status = "running",
            CreatedAt = staleAt,
            UpdatedAt = staleAt
        });

        using var service = CreateService(
            queue,
            runtime,
            state,
            operations,
            startupRecoveryThreshold: TimeSpan.FromSeconds(1));

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => state.StatusFor("qwen2.5:7b") == "failed");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal("failed", state.StatusFor("qwen2.5:7b"));
        Assert.Contains("recovered as failed", state.Current?.LastErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("failed", operations.Get(operationId)?.Status);
    }

    private static ModelDownloadHostedService CreateService(
        ChannelModelDownloadQueue queue,
        StubRuntimeClient runtime,
        InMemoryStateRepository state,
        InMemoryOperationStore operations,
        TimeSpan? startupRecoveryThreshold = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ModelDownloadHostedService(
            queue,
            runtime,
            state,
            operations,
            new AppReadinessStateService(),
            "Host=unused;Database=unused",
            services,
                NullLogger<ModelDownloadHostedService>.Instance,
                startupRecoveryThreshold);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class StubRuntimeClient : IModelRuntimeClient
    {
        public List<ModelPullProgress> Progress { get; set; } = [];
        public List<ModelPresence> Installed { get; set; } = [];
        public Exception? PullException { get; set; }
        public TaskCompletionSource? ProgressGate { get; set; }
        public Func<string, Task>? OnPull { get; set; }
        public bool PullStarted;
        public int PullCallCount;

        public async IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            PullStarted = true;
            PullCallCount++;
            if (OnPull is not null)
            {
                await OnPull(model);
            }

            if (PullException is not null)
            {
                throw PullException;
            }

            foreach (var item in Progress)
            {
                if (ProgressGate is not null)
                {
                    await ProgressGate.Task;
                }

                yield return item;
            }
        }

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelPresence>>(Installed);

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModelRuntimeInfo("ollama", model, null, []));
    }

    private sealed class InMemoryStateRepository : IModelRuntimeStateRepository
    {
        private readonly Dictionary<string, ModelRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
        public List<string> History { get; } = [];
        public ModelRuntimeState? Current => _states.Values.LastOrDefault();

        public void Seed(ModelRuntimeState state) => _states[state.ModelId] = state;

        public Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
        {
            History.Add(state.Status);
            _states[state.ModelId] = state;
            return Task.CompletedTask;
        }

        public Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult(_states.TryGetValue(modelId, out var state) ? state : null);

        public Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(_states.Values.Where(s => string.Equals(s.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(_states.Values.ToList());

        public string? StatusFor(string modelId) => _states.TryGetValue(modelId, out var state) ? state.Status : null;
    }

    private sealed class InMemoryOperationStore : IOperationStore
    {
        private readonly Dictionary<Guid, OperationRecord> _operations = [];

        public void Seed(OperationRecord operation) => _operations[operation.Id] = operation;

        public Task PersistAsync(OperationRecord operation, CancellationToken cancellationToken = default)
        {
            _operations[operation.Id] = operation;
            return Task.CompletedTask;
        }

        public Task<OperationRecord?> GetByIdAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.FromResult(_operations.TryGetValue(operationId, out var operation) ? operation : null);

        public Task UpdateHangfireJobIdAsync(Guid operationId, string hangfireJobId, CancellationToken cancellationToken = default)
        {
            if (_operations.TryGetValue(operationId, out var operation))
            {
                operation.HangfireJobId = hangfireJobId;
            }

            return Task.CompletedTask;
        }

        public Task<List<OperationRecord>> GetActiveByTypeAsync(string operationType, CancellationToken cancellationToken = default)
            => Task.FromResult(_operations.Values
                .Where(o => o.OperationType == operationType && o.Status is "queued" or "running")
                .ToList());

        public Task<OperationRecord?> GetLastCompletedByTypeAsync(string operationType, CancellationToken cancellationToken = default)
            => Task.FromResult(_operations.Values
                .Where(o => o.OperationType == operationType && o.Status == "completed")
                .OrderByDescending(o => o.CompletedAt)
                .Cast<OperationRecord?>()
                .FirstOrDefault());

        public OperationRecord? Get(Guid id) => _operations.TryGetValue(id, out var operation) ? operation : null;
    }
}
