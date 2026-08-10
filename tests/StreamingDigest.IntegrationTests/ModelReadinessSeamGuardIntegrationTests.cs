using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector.Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using Xunit;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// WS-7 seam-guard integration gate (plan §9.3): with the embedding model unready, the S1
/// document/embedding store defers embeddings (pending rows, documents still text-searchable)
/// and the S2 recent-search store persists the search without a query vector — never a 500.
/// </summary>
[Collection("PostgreSQL Integration Tests")]
public sealed class ModelReadinessSeamGuardIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-seam-guard-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task S1_store_defers_embeddings_when_model_unready_without_throwing()
    {
        var guard = new FakeModelReadinessGuard(Unready());
        var notifier = new ModelReadinessNotifier(new TestLogger<ModelReadinessNotifier>());
        var embeddingService = new ThrowingEmbeddingService();

        await using var store = new PostgresSearchDocumentEmbeddingStore(
            _connectionString!,
            embeddingService,
            guard,
            notifier);

        var document = new GeneratedSearchDocument(
            DocumentType: "video_metadata",
            SourceEntityType: "video",
            SourceEntityId: Guid.NewGuid(),
            ParentVideoId: null,
            TitleEffective: "Unready embedding model test",
            BodyEffective: "body content for text search",
            ContentHash: "hash-s1-deferred");

        // Should not throw despite the embedding service always throwing and the model unready.
        var stored = await store.StoreAsync([document]);

        Assert.Empty(stored); // no embeddings produced
        Assert.Equal(0, embeddingService.CallCount);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Document still persisted (text-searchable).
        await using var documentCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM public.search_documents WHERE content_hash = 'hash-s1-deferred';",
            connection);
        Assert.Equal(1L, (long)(await documentCommand.ExecuteScalarAsync())!);

        // A deferred (pending) embedding row exists with NULL vector.
        await using var embeddingCommand = new NpgsqlCommand(
            """
            SELECT embedding_status, embedding IS NULL, error_summary
            FROM public.embeddings
            WHERE content_hash = 'hash-s1-deferred';
            """,
            connection);
        await using var reader = await embeddingCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("pending", reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Contains("deferred", reader.GetString(2), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task S2_store_persists_search_without_vector_when_model_unready_without_throwing()
    {
        var guard = new FakeModelReadinessGuard(Unready());
        var notifier = new ModelReadinessNotifier(new TestLogger<ModelReadinessNotifier>());
        var embeddingService = new ThrowingEmbeddingService();

        await using var store = new PostgresRecentSearchStore(
            _connectionString!,
            embeddingService,
            guard,
            notifier);

        // Should not throw despite the embedding service always throwing and the model unready.
        var stored = await store.StoreSearchAsync(
            "a query with no embedding model",
            new SearchFilters(),
            new SearchUiSettings());

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(0, embeddingService.CallCount);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Recent search persisted.
        await using var searchCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM public.recent_searches WHERE id = @id;",
            connection);
        searchCommand.Parameters.AddWithValue("id", stored.Id);
        Assert.Equal(1L, (long)(await searchCommand.ExecuteScalarAsync())!);

        // No query embedding was written for the search.
        await using var embeddingCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM public.search_query_embeddings WHERE recent_search_id = @id;",
            connection);
        embeddingCommand.Parameters.AddWithValue("id", stored.Id);
        Assert.Equal(0L, (long)(await embeddingCommand.ExecuteScalarAsync())!);
    }

    private static ModelReadiness Unready() => new(
        RuntimeRole.Embedding,
        "ollama",
        "bge-m3",
        IsReady: false,
        Status: "missing",
        Reason: "Embedding model is not ready.");

    private sealed class FakeModelReadinessGuard : IModelReadinessGuard
    {
        private readonly ModelReadiness _readiness;

        public FakeModelReadinessGuard(ModelReadiness readiness) => _readiness = readiness;

        public Task<ModelReadiness> CheckAsync(RuntimeRole role, CancellationToken cancellationToken = default)
            => Task.FromResult(_readiness);
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Embedding service must not be invoked when the model is unready.");
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run", "-d",
            "--name", _containerName,
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the seam-guard integration test container.");
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
        Exception? lastError = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for the PostgreSQL seam-guard integration test container to become ready. Last error: {lastError?.GetType().Name}: {lastError?.Message}");
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
}
