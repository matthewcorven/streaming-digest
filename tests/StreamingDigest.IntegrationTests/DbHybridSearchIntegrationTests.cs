using System.Diagnostics;
using System.Globalization;
using System.Text;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;
using Xunit;

namespace StreamingDigest.IntegrationTests;

[Collection("PostgreSQL Integration Tests")]
public sealed class DbHybridSearchIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-db-hybrid-search-tests-{Guid.NewGuid():N}";
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
    public async Task Empty_corpus_returns_a_waiting_state_without_fabricated_results()
    {
        await using var searcher = new PostgresSearchCorpusSearcher(_connectionString!);
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store, searcher, new FakeVideoClusterEmbeddingStore());

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "hybrid search",
            Filters = new SearchFilters { ResultType = "video" }
        });

        Assert.Empty(response.Results);
        Assert.Contains("No searchable corpus yet", response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Seeded_corpus_returns_one_cluster_per_video_with_real_scores()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync();

        var channelId = Guid.NewGuid();
        await InsertChannelAsync(connection, channelId, "Hybrid Channel");
        var firstVideo = Guid.NewGuid();
        var secondVideo = Guid.NewGuid();
        await InsertVideoAsync(connection, firstVideo, channelId, "Building a hybrid search engine", "hybrid search overview");
        await InsertVideoAsync(connection, secondVideo, channelId, "Unrelated gardening video", "gardening tips and tricks");

        // Document for the first video matches the query text; the second does not.
        var matchingDocument = Guid.NewGuid();
        await InsertSearchDocumentAsync(connection, matchingDocument, firstVideo, "video_metadata", "Building a hybrid search engine", "hybrid search overview");
        var otherDocument = Guid.NewGuid();
        await InsertSearchDocumentAsync(connection, otherDocument, secondVideo, "video_metadata", "Unrelated gardening video", "gardening tips");

        await using var searcher = new PostgresSearchCorpusSearcher(_connectionString!);
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store, searcher, new FakeVideoClusterEmbeddingStore());

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "hybrid search",
            Filters = new SearchFilters { ResultType = "video" }
        });

        Assert.NotEmpty(response.Results);
        Assert.Single(response.Results);
        Assert.Equal(firstVideo, response.Results[0].VideoId);
        Assert.NotEmpty(response.Results[0].ScoreExplanation);
    }

    private static async Task InsertChannelAsync(NpgsqlConnection connection, Guid channelId, string name)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.channels (id, youtube_channel_id, name_original, profile_url, source_url)
            VALUES (@id, @youtube_channel_id, @name, @profile_url, @source_url);
            """,
            connection);
        command.Parameters.AddWithValue("id", channelId);
        command.Parameters.AddWithValue("youtube_channel_id", $"uc-{channelId:N}");
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("profile_url", $"https://example.com/{channelId:N}");
        command.Parameters.AddWithValue("source_url", $"https://example.com/{channelId:N}/about");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVideoAsync(NpgsqlConnection connection, Guid videoId, Guid channelId, string title, string description)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.videos (
                id, platform, platform_video_url, platform_video_id, youtube_video_id,
                channel_id, author_original, title_original, description_original, video_url,
                transcript_status, ingestion_status
            )
            VALUES (
                @id, 'youtube', @platform_video_url, @platform_video_id, @youtube_video_id,
                @channel_id, @author, @title, @description, @video_url,
                'processed', 'processed'
            );
            """,
            connection);
        command.Parameters.AddWithValue("id", videoId);
        command.Parameters.AddWithValue("platform_video_url", $"https://youtube.com/watch?v={videoId:N}");
        command.Parameters.AddWithValue("platform_video_id", videoId.ToString("N", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("youtube_video_id", videoId.ToString("N", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("channel_id", channelId);
        command.Parameters.AddWithValue("author", "Author");
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("video_url", $"https://youtube.com/watch?v={videoId:N}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSearchDocumentAsync(NpgsqlConnection connection, Guid documentId, Guid videoId, string documentType, string title, string body)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.search_documents (
                id, document_type, source_entity_type, source_entity_id,
                parent_video_id, title_effective, body_effective, content_hash
            )
            VALUES (
                @id, @document_type, 'video', @video_id,
                @video_id, @title, @body, @content_hash
            );
            """,
            connection);
        command.Parameters.AddWithValue("id", documentId);
        command.Parameters.AddWithValue("document_type", documentType);
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("content_hash", $"{documentId:N}");
        await command.ExecuteNonQueryAsync();

        // Embedding row is required for readiness (searcher counts docs with succeeded embeddings).
        await using var embeddingCommand = new NpgsqlCommand(
            """
            INSERT INTO public.embeddings (
                id, search_document_id, provider, model, dimensions, content_hash, embedding, embedding_status
            )
            VALUES (
                @id, @search_document_id, 'test-provider', 'test-model', 3, @content_hash, @embedding, 'succeeded'
            );
            """,
            connection);
        embeddingCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        embeddingCommand.Parameters.AddWithValue("search_document_id", documentId);
        embeddingCommand.Parameters.AddWithValue("content_hash", $"{documentId:N}");
        embeddingCommand.Parameters.AddWithValue("embedding", new Vector(new float[] { 1f, 2f, 3f }));
        await embeddingCommand.ExecuteNonQueryAsync();
    }

    private async Task StartPostgresContainerAsync()
    {
        var passwordArgs = new StringBuilder()
            .Append($"POSTGRES_PASSWORD={Password}")
            .ToString();

        var dockerArgs = new[]
        {
            "run", "-d",
            "--name", _containerName,
            "-e", passwordArgs,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the DB hybrid search integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL DB hybrid search integration test container to become ready.");
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

    private sealed class InMemoryRecentSearchStore : IRecentSearchStore
    {
        public Task<StoredRecentSearch> StoreSearchAsync(string query, SearchFilters filters, SearchUiSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(new StoredRecentSearch(Guid.NewGuid(), query, DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<string>> ListRecentQueriesAsync(int take = 8, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordInteractionAsync(SearchInteractionEvent interaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, int>> GetRecentOpenCountsAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

        public Task<StoredQueryEmbedding?> GetQueryEmbeddingAsync(Guid recentSearchId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoredQueryEmbedding?>(null);
    }

    private sealed class FakeVideoClusterEmbeddingStore : IVideoClusterEmbeddingStore
    {
        public Task<IReadOnlyList<StoredVideoClusterEmbedding>> BuildForVideoAsync(Guid videoId, Guid? generatedByOperationId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkStaleForVideoAsync(Guid videoId, Guid? markedByOperationId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<VideoClusterHighSignalMatch>> GetHighSignalMatchesAsync(Guid videoId, double thresholdPercent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<VideoClusterRelatedItem>> GetRelatedVideosAsync(Guid videoId, int take = 3, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VideoClusterRelatedItem>>(Array.Empty<VideoClusterRelatedItem>());
    }
}
