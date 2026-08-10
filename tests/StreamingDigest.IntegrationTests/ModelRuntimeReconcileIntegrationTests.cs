using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// WS-3 (S10) integration gate: startup reconcile maps /api/tags presence into
/// <c>model_runtime_state</c> against real PostgreSQL. Runtime is stubbed; schema is the real
/// <see cref="ModelRuntimeStateSchemaGuard"/>. Skip-by-default per repo convention.
/// </summary>
public sealed class ModelRuntimeReconcileIntegrationTests : IAsyncLifetime
{
    private string _connectionString = null!;
    private IModelRuntimeStateRepository _repository = null!;
    private IModelRuntimeStateSchemaGuard _schemaGuard = null!;

    public async Task InitializeAsync()
    {
        _connectionString = "Server=localhost;Port=5432;Database=streamingdigest_test;User Id=streamingdigest;******";
        _repository = new PostgresModelRuntimeStateRepository(_connectionString);
        _schemaGuard = new ModelRuntimeStateSchemaGuard();

        await _schemaGuard.EnsureSchemaAsync(_connectionString);
        await ClearTableAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await ClearTableAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task ReconcileAsync_PrePresentModelsBecomeReady_InRealDatabase()
    {
        var client = new StubRuntimeClient(
            new ModelPresence("ollama", "bge-m3", "sha256:abc", 1234),
            new ModelPresence("ollama", "llama3.1:8b", "sha256:def", 5678));
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(_repository);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.PresentCount);
        Assert.Equal(2, result.MarkedReadyCount);

        var embedding = await _repository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
        Assert.NotNull(embedding);
        Assert.Equal("ready", embedding!.Status);
        Assert.Equal("embedding", embedding.RuntimeRole);
        Assert.NotNull(embedding.LastSeenInRuntimeAt);
        Assert.NotNull(embedding.LastVerifiedAt);

        var llm = await _repository.GetByProviderAndModelIdAsync("ollama", "llama3.1:8b");
        Assert.NotNull(llm);
        Assert.Equal("ready", llm!.Status);
        Assert.Equal("llm", llm.RuntimeRole);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task ReconcileAsync_PreservesInFlightRow_InRealDatabase()
    {
        await _repository.UpsertAsync(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "downloading",
            CurrentOperationId = Guid.NewGuid(),
            ProgressPercent = 10,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var client = new StubRuntimeClient(new ModelPresence("ollama", "bge-m3", "sha256:abc", 1234));
        var service = new ModelRuntimeReconcileService(client);

        var result = await service.ReconcileAsync(_repository);

        Assert.Equal(1, result.RefreshedInFlightCount);
        var state = await _repository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
        Assert.NotNull(state);
        Assert.Equal("downloading", state!.Status);
        Assert.NotNull(state.CurrentOperationId);
        Assert.NotNull(state.LastSeenInRuntimeAt);
    }

    private async Task ClearTableAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM model_runtime_state", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class StubRuntimeClient : IModelRuntimeClient
    {
        private readonly IReadOnlyList<ModelPresence> _installed;

        public StubRuntimeClient(params ModelPresence[] installed) => _installed = installed;

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_installed);

        public IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
