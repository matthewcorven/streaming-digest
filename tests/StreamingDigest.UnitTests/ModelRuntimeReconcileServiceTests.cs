using System.Text.Json;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using Xunit;

namespace StreamingDigest.UnitTests;

public sealed class ModelRuntimeReconcileServiceTests
{
    [Fact]
    public async Task ReconcileAsync_MarksCatalogModelReady_WhenPresentInRuntime()
    {
        var client = new StubRuntimeClient(new ModelPresence("ollama", "bge-m3", "sha256:abc", 1234));
        var repository = new StubStateRepository();
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.True(result.Succeeded);
        Assert.True(result.RuntimeReachable);
        Assert.Equal(1, result.PresentCount);
        Assert.Equal(1, result.MarkedReadyCount);

        var state = Assert.Single(repository.States.Values);
        Assert.Equal("ollama", state.Provider);
        Assert.Equal("bge-m3", state.ModelId);
        Assert.Equal("embedding", state.RuntimeRole);
        Assert.Equal("ready", state.Status);
        Assert.NotNull(state.LastSeenInRuntimeAt);
        Assert.NotNull(state.LastVerifiedAt);
        Assert.Null(state.CurrentOperationId);
        Assert.Null(state.ProgressPercent);
        Assert.Null(state.LastErrorSummary);

        using var details = JsonDocument.Parse(state.DetailsJson!);
        Assert.Equal("sha256:abc", details.RootElement.GetProperty("digest").GetString());
        Assert.Equal(1234, details.RootElement.GetProperty("sizeInBytes").GetInt64());
        Assert.True(details.RootElement.GetProperty("inCatalog").GetBoolean());
        Assert.Equal("startup", details.RootElement.GetProperty("reconciledFrom").GetString());
    }

    [Fact]
    public async Task ReconcileAsync_NormalizesImplicitLatestTag_ToCatalogModelId()
    {
        var existing = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "failed",
            LastErrorSummary = "previous failure",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var client = new StubRuntimeClient(new ModelPresence("ollama", "bge-m3:latest", "sha256:abc", 1234));
        var repository = new StubStateRepository(existing);
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.True(result.Succeeded);
        var state = repository.States[("ollama", "bge-m3")];
        Assert.Equal("ready", state.Status);
        Assert.Equal("bge-m3", state.ModelId);
        Assert.Null(state.LastErrorSummary);
        using var details = JsonDocument.Parse(state.DetailsJson!);
        Assert.Equal("sha256:abc", details.RootElement.GetProperty("digest").GetString());
    }

    [Fact]
    public async Task ReconcileAsync_SetsUnknownRuntimeRole_ForModelAbsentFromCatalog()
    {
        var client = new StubRuntimeClient(new ModelPresence("ollama", "user-side-pull:7b", null, null));
        var repository = new StubStateRepository();
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.True(result.Succeeded);
        var state = Assert.Single(repository.States.Values);
        Assert.Equal("unknown", state.RuntimeRole);
        Assert.Equal("ready", state.Status);
    }

    [Fact]
    public async Task ReconcileAsync_PreservesInFlightState_OnlyRefreshesLastSeen()
    {
        var operationId = Guid.NewGuid();
        var inFlight = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "llama3.1:8b",
            RuntimeRole = "llm",
            Status = "downloading",
            CurrentOperationId = operationId,
            ProgressPercent = 42,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var client = new StubRuntimeClient(new ModelPresence("ollama", "llama3.1:8b", "sha256:def", 999));
        var repository = new StubStateRepository(inFlight);
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.MarkedReadyCount);
        Assert.Equal(1, result.RefreshedInFlightCount);

        var state = repository.States[("ollama", "llama3.1:8b")];
        Assert.Equal("downloading", state.Status);
        Assert.Equal(operationId, state.CurrentOperationId);
        Assert.Equal(42, state.ProgressPercent);
        Assert.NotNull(state.LastSeenInRuntimeAt);
        Assert.True(state.LastSeenInRuntimeAt > inFlight.UpdatedAt.AddMinutes(-1));
        Assert.Null(state.LastVerifiedAt);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("running")]
    public async Task ReconcileAsync_PreservesQueuedAndRunningStatuses(string status)
    {
        var existing = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "qwen2.5:7b",
            RuntimeRole = "llm",
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        };
        var client = new StubRuntimeClient(new ModelPresence("ollama", "qwen2.5:7b", null, null));
        var repository = new StubStateRepository(existing);
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.Equal(1, result.RefreshedInFlightCount);
        Assert.Equal(status, repository.States[("ollama", "qwen2.5:7b")].Status);
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotResetLastVerifiedAt_WhenAlreadyReady()
    {
        var verifiedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var existing = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "ready",
            LastVerifiedAt = verifiedAt,
            UpdatedAt = verifiedAt
        };
        var client = new StubRuntimeClient(new ModelPresence("ollama", "bge-m3", null, null));
        var repository = new StubStateRepository(existing);
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.Equal(1, result.MarkedReadyCount);
        Assert.Equal(verifiedAt, repository.States[("ollama", "bge-m3")].LastVerifiedAt);
    }

    [Fact]
    public async Task ReconcileAsync_TransitionsFailedStateToReady_WhenPresent()
    {
        var existing = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "failed",
            LastErrorSummary = "pull failed",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var client = new StubRuntimeClient(new ModelPresence("ollama", "bge-m3", null, null));
        var repository = new StubStateRepository(existing);
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        var state = repository.States[("ollama", "bge-m3")];
        Assert.Equal("ready", state.Status);
        Assert.Null(state.LastErrorSummary);
        Assert.NotNull(state.LastVerifiedAt);
    }

    [Fact]
    public async Task ReconcileAsync_IgnoresNonOllamaProviders()
    {
        var client = new StubRuntimeClient(
            new ModelPresence("external", "external-embedding-model", null, null),
            new ModelPresence("whisper", "whisper", null, null));
        var repository = new StubStateRepository();
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.PresentCount);
        Assert.Empty(repository.States);
    }

    [Fact]
    public async Task ReconcileAsync_ReturnsRuntimeUnreachable_WhenTagsCallThrows()
    {
        var client = new StubRuntimeClient(new HttpRequestException("connection refused"));
        var repository = new StubStateRepository();
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.False(result.Succeeded);
        Assert.False(result.RuntimeReachable);
        Assert.Equal("connection refused", result.ErrorSummary);
        Assert.Empty(repository.States);
    }

    [Fact]
    public async Task ReconcileAsync_ContinuesAfterPersistFailure_AndReportsIt()
    {
        var client = new StubRuntimeClient(
            new ModelPresence("ollama", "bge-m3", null, null),
            new ModelPresence("ollama", "llama3.1:8b", null, null));
        var repository = new StubStateRepository { FailOnModelId = "bge-m3" };
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(repository);

        Assert.False(result.Succeeded);
        Assert.True(result.RuntimeReachable);
        Assert.Equal(2, result.PresentCount);
        Assert.Equal(1, result.MarkedReadyCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains("bge-m3", result.ErrorSummary);
        Assert.True(repository.States.ContainsKey(("ollama", "llama3.1:8b")));
    }

    private sealed class StubRuntimeClient : IModelRuntimeClient
    {
        private readonly IReadOnlyList<ModelPresence> _installed;
        private readonly Exception? _throwOnList;

        public StubRuntimeClient(params ModelPresence[] installed) => _installed = installed;

        public StubRuntimeClient(Exception throwOnList)
        {
            _installed = Array.Empty<ModelPresence>();
            _throwOnList = throwOnList;
        }

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => _throwOnList is not null
                ? Task.FromException<IReadOnlyList<ModelPresence>>(_throwOnList)
                : Task.FromResult(_installed);

        public IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubStateRepository : IModelRuntimeStateRepository
    {
        public Dictionary<(string Provider, string ModelId), ModelRuntimeState> States { get; } = new();

        public string? FailOnModelId { get; init; }

        public StubStateRepository(params ModelRuntimeState[] seed)
        {
            foreach (var state in seed)
            {
                States[(state.Provider, state.ModelId)] = state;
            }
        }

        public Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
        {
            if (FailOnModelId == modelId)
            {
                return Task.FromException<ModelRuntimeState?>(new InvalidOperationException("persist failed"));
            }

            States.TryGetValue((provider, modelId), out var state);
            return Task.FromResult(state);
        }

        public Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
        {
            if (FailOnModelId == state.ModelId)
            {
                return Task.FromException(new InvalidOperationException("persist failed"));
            }

            States[(state.Provider, state.ModelId)] = state;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(States.Values.Where(s => s.Provider == provider).ToList());

        public Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(States.Values.ToList());
    }
}
