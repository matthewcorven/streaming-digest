using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

public sealed class ModelRuntimeStateRepositoryIntegrationTests : IAsyncLifetime
{
    private string _connectionString = null!;
    private IModelRuntimeStateRepository _repository = null!;
    private IModelRuntimeStateSchemaGuard _schemaGuard = null!;

    public async Task InitializeAsync()
    {
        // Use test database connection
        _connectionString = "Server=localhost;Port=5432;Database=streamingdigest_test;User Id=streamingdigest;Password=password";
        _repository = new PostgresModelRuntimeStateRepository(_connectionString);
        _schemaGuard = new ModelRuntimeStateSchemaGuard();

        // Ensure schema exists
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
    public async Task EnsureSchemaAsync_CreatesTableAndIndexes()
    {
        // Act
        await _schemaGuard.EnsureSchemaAsync(_connectionString);

        // Assert - verify table exists
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'model_runtime_state'
            );
            """,
            connection);

        var tableExists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(tableExists, "model_runtime_state table should exist");
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task EnsureSchemaAsync_CreatesUniqueIndex()
    {
        // Act
        await _schemaGuard.EnsureSchemaAsync(_connectionString);

        // Assert - verify unique index exists
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.table_constraints
                WHERE table_schema = 'public'
                AND table_name = 'model_runtime_state'
                AND constraint_name = 'uq_model_runtime_state_provider_model_id'
                AND constraint_type = 'UNIQUE'
            );
            """,
            connection);

        var indexExists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(indexExists, "Unique index on (provider, model_id) should exist");
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task UpsertAsync_InsertsNewState()
    {
        // Arrange
        var state = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = "ready",
            LastVerifiedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _repository.UpsertAsync(state);

        // Assert
        var retrieved = await _repository.GetByProviderAndModelIdAsync(state.Provider, state.ModelId);
        Assert.NotNull(retrieved);
        Assert.Equal(state.Id, retrieved!.Id);
        Assert.Equal("ready", retrieved.Status);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task UpsertAsync_UpdatesExistingState()
    {
        // Arrange - insert initial state
        var provider = "ollama";
        var modelId = "llama3.1:8b";
        var stateId = Guid.NewGuid();
        var initialState = new ModelRuntimeState
        {
            Id = stateId,
            Provider = provider,
            ModelId = modelId,
            RuntimeRole = "llm",
            Status = "queued",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repository.UpsertAsync(initialState);

        // Act - update status
        var updatedState = new ModelRuntimeState
        {
            Id = stateId,
            Provider = provider,
            ModelId = modelId,
            RuntimeRole = "llm",
            Status = "ready",
            ProgressPercent = 100,
            LastVerifiedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        };
        await _repository.UpsertAsync(updatedState);

        // Assert
        var retrieved = await _repository.GetByProviderAndModelIdAsync(provider, modelId);
        Assert.NotNull(retrieved);
        Assert.Equal("ready", retrieved!.Status);
        Assert.Equal(100, retrieved.ProgressPercent);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task UniqueConstraint_EnforcesOnProviderAndModelId()
    {
        // Arrange
        var provider = "ollama";
        var modelId = "bge-m3";
        var state1 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = modelId,
            RuntimeRole = "embedding",
            Status = "unknown",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.UpsertAsync(state1);

        // Act & Assert - attempting to insert with same (provider, modelId) should use conflict handling
        var state2 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = modelId,
            RuntimeRole = "embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // This should not throw, it should update due to ON CONFLICT
        await _repository.UpsertAsync(state2);

        // Verify only one row exists
        var all = await _repository.GetByProviderAsync(provider);
        Assert.Single(all);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task GetByProviderAndModelIdAsync_ReturnsNullWhenNotFound()
    {
        // Act
        var result = await _repository.GetByProviderAndModelIdAsync("ollama", "nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task GetByProviderAsync_ReturnsAllModelsForProvider()
    {
        // Arrange
        var provider = "ollama";
        var state1 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = "model1",
            RuntimeRole = "embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var state2 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = "model2",
            RuntimeRole = "llm",
            Status = "unknown",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.UpsertAsync(state1);
        await _repository.UpsertAsync(state2);

        // Act
        var result = await _repository.GetByProviderAsync(provider);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.ModelId == "model1");
        Assert.Contains(result, s => s.ModelId == "model2");
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task GetAllAsync_ReturnsAllStates()
    {
        // Arrange
        var state1 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "model1",
            RuntimeRole = "embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var state2 = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "whisper",
            ModelId = "audio-model",
            RuntimeRole = "audio",
            Status = "unknown",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.UpsertAsync(state1);
        await _repository.UpsertAsync(state2);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        Assert.True(result.Count >= 2);
    }

    [Fact(Skip = "Requires PostgreSQL infrastructure; runs locally only")]
    public async Task Upsert_PreservesNullFields()
    {
        // Arrange
        var state = new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "test-model",
            RuntimeRole = "embedding",
            Status = "unknown",
            CurrentOperationId = null,
            ProgressPercent = null,
            LastVerifiedAt = null,
            LastErrorSummary = null,
            DetailsJson = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _repository.UpsertAsync(state);
        var retrieved = await _repository.GetByProviderAndModelIdAsync(state.Provider, state.ModelId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Null(retrieved!.CurrentOperationId);
        Assert.Null(retrieved.ProgressPercent);
        Assert.Null(retrieved.LastVerifiedAt);
        Assert.Null(retrieved.LastErrorSummary);
    }

    private async Task ClearTableAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand("TRUNCATE TABLE public.model_runtime_state;", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // Relation not found
        {
            // Table doesn't exist yet, that's okay
        }
    }
}
