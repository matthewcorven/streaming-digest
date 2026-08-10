using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// WS-4 (#203): verify runs a real runtime probe, persists the outcome in
/// model_runtime_state, and projects app_readiness_checks from verified presence only.
/// </summary>
public sealed class ModelVerificationIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "Server=localhost;Port=5432;Database=streamingdigest_test;User Id=streamingdigest;******";

    private IModelRuntimeStateRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new PostgresModelRuntimeStateRepository(ConnectionString);
        await new ModelRuntimeStateSchemaGuard().EnsureSchemaAsync(ConnectionString);
        await new AppReadinessStateService().EnsureSchemaAsync(ConnectionString);
        await ClearTablesAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await ClearTablesAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task VerifyModelAsync_ModelPresent_RecordsReadyStateAndSucceededReadiness()
    {
        var runtimeClient = new StubModelRuntimeClient(new ModelPresence("ollama", "bge-m3", "sha256:abc", 1234));
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, _repository);

        var result = await service.VerifyModelAsync(ConnectionString, "embedding", "bge-m3");

        Assert.True(result.Verified);
        Assert.Equal("verified", result.Status);

        var state = await _repository.GetByProviderAndModelIdAsync("ollama", "bge-m3");
        Assert.NotNull(state);
        Assert.Equal("ready", state!.Status);
        Assert.Equal("embedding", state.RuntimeRole);
        Assert.NotNull(state.LastVerifiedAt);
        Assert.NotNull(state.LastSeenInRuntimeAt);
        Assert.Null(state.LastErrorSummary);

        var readiness = await GetReadinessCheckAsync("embedding_model_verified");
        Assert.NotNull(readiness);
        Assert.Equal("succeeded", readiness.Value.Status);
        Assert.Null(readiness.Value.LastErrorSummary);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task VerifyModelAsync_ModelMissing_RecordsFailedStateAndFailedReadiness()
    {
        var runtimeClient = new StubModelRuntimeClient();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, _repository);

        var result = await service.VerifyModelAsync(ConnectionString, "llm", "llama3.1:8b");

        Assert.False(result.Verified);
        Assert.Equal("failed", result.Status);

        var state = await _repository.GetByProviderAndModelIdAsync("ollama", "llama3.1:8b");
        Assert.NotNull(state);
        Assert.Equal("failed", state!.Status);
        Assert.Null(state.LastVerifiedAt);
        Assert.NotNull(state.LastErrorSummary);

        var readiness = await GetReadinessCheckAsync("llm_model_verified");
        Assert.NotNull(readiness);
        Assert.Equal("failed", readiness.Value.Status);
        Assert.NotNull(readiness.Value.LastErrorSummary);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task VerifyModelAsync_RuntimeUnreachable_ProjectsFailureNotSuccess()
    {
        var runtimeClient = new StubModelRuntimeClient(new InvalidOperationException("connection refused"));
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, _repository);

        var result = await service.VerifyModelAsync(ConnectionString, "embedding", "bge-m3");

        Assert.False(result.Verified);

        var readiness = await GetReadinessCheckAsync("embedding_model_verified");
        Assert.NotNull(readiness);
        Assert.Equal("failed", readiness.Value.Status);
    }

    private static async Task<(string Status, string? LastErrorSummary)?> GetReadinessCheckAsync(string checkKey)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT status, last_error_summary FROM public.app_readiness_checks WHERE check_key = @checkKey;",
            connection);
        command.Parameters.AddWithValue("checkKey", checkKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task ClearTablesAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                "TRUNCATE TABLE public.model_runtime_state; TRUNCATE TABLE public.app_readiness_checks;",
                connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // Relation not found
        {
            // Tables don't exist yet, that's okay
        }
    }

    private sealed class StubModelRuntimeClient : IModelRuntimeClient
    {
        private readonly IReadOnlyList<ModelPresence> _installed;
        private readonly Exception? _failure;

        public StubModelRuntimeClient(params ModelPresence[] installed) => _installed = installed;

        public StubModelRuntimeClient(Exception failure) : this() => _failure = failure;

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => _failure is not null ? Task.FromException<IReadOnlyList<ModelPresence>>(_failure) : Task.FromResult(_installed);

        public IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
