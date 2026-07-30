using System.Diagnostics;
using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class PostgresRecentSearchStoreTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:pg17";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-recent-search-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();
        await new PostgresMigrationRunner(_connectionString).ApplyAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task StoreSearchAsync_persists_recent_search_and_query_embedding()
    {
        var store = new PostgresRecentSearchStore(_connectionString!, new StubEmbeddingService());

        var stored = await store.StoreSearchAsync(
            "semantic ranking",
            new SearchFilters { Channel = "Tonbis AI Garage", HasNotes = true },
            new SearchUiSettings { TextWeight = 0.4, VectorWeight = 0.6 });

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                rs.query_text,
                rs.text_weight,
                rs.vector_weight,
                rs.filters_json->>'channel',
                rs.filters_json->>'hasNotes',
                sqe.provider,
                sqe.model,
                sqe.dimensions
            FROM public.recent_searches AS rs
            INNER JOIN public.search_query_embeddings AS sqe
                ON sqe.recent_search_id = rs.id
            WHERE rs.id = @recentSearchId;
            """,
            connection);
        command.Parameters.AddWithValue("recentSearchId", stored.Id);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("semantic ranking", reader.GetString(0));
        Assert.Equal(0.4m, reader.GetDecimal(1));
        Assert.Equal(0.6m, reader.GetDecimal(2));
        Assert.Equal("Tonbis AI Garage", reader.GetString(3));
        Assert.Equal("true", reader.GetString(4));
        Assert.Equal("ollama", reader.GetString(5));
        Assert.Equal("test-model", reader.GetString(6));
        Assert.Equal(3, reader.GetInt32(7));
    }

    [Fact]
    public async Task ClearRecentSearchesAsync_removes_search_history_but_retains_interactions()
    {
        var videoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await InsertVideoAsync(videoId);

        var store = new PostgresRecentSearchStore(_connectionString!, new StubEmbeddingService());
        var stored = await store.StoreSearchAsync(
            "project idea search",
            SearchFilters.Empty,
            SearchUiSettings.Default);

        await store.RecordInteractionAsync(new SearchInteractionEvent(
            stored.Id,
            videoId,
            null,
            "video",
            "result_opened",
            null,
            DateTimeOffset.UtcNow));

        await store.ClearRecentSearchesAsync();

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM public.recent_searches),
                (SELECT COUNT(*) FROM public.search_query_embeddings),
                (SELECT COUNT(*) FROM public.user_interaction_events),
                (SELECT COUNT(*) FROM public.user_interaction_events WHERE recent_search_id IS NULL);
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
    }

    [Fact]
    public async Task RecordInteractionAsync_updates_recent_open_counts_used_for_ranking()
    {
        var firstVideoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var secondVideoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        await InsertVideoAsync(firstVideoId);
        await InsertVideoAsync(secondVideoId);

        var store = new PostgresRecentSearchStore(_connectionString!, new StubEmbeddingService());

        await store.RecordInteractionAsync(new SearchInteractionEvent(
            null,
            firstVideoId,
            null,
            "video",
            "result_opened",
            null,
            DateTimeOffset.UtcNow));
        await store.RecordInteractionAsync(new SearchInteractionEvent(
            null,
            firstVideoId,
            null,
            "video",
            "website_opened",
            null,
            DateTimeOffset.UtcNow));
        await store.RecordInteractionAsync(new SearchInteractionEvent(
            null,
            secondVideoId,
            null,
            "video",
            "result_opened",
            null,
            DateTimeOffset.UtcNow.AddDays(-120)));

        var counts = await store.GetRecentOpenCountsAsync([firstVideoId, secondVideoId]);

        Assert.Equal(2, counts[firstVideoId]);
        Assert.False(counts.ContainsKey(secondVideoId));
    }

    private async Task InsertVideoAsync(Guid videoId)
    {
        var channelId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using (var channelCommand = new NpgsqlCommand(
            """
            INSERT INTO public.channels (
                id,
                youtube_channel_id,
                name_original,
                profile_url,
                source_url,
                created_at,
                updated_at
            )
            VALUES (
                @channelId,
                @youtubeChannelId,
                'Recent Search Test Channel',
                'https://example.com/channel',
                'https://example.com/source',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
            """,
            connection))
        {
            channelCommand.Parameters.AddWithValue("channelId", channelId);
            channelCommand.Parameters.AddWithValue("youtubeChannelId", $"recent-search-test-{channelId:N}");
            await channelCommand.ExecuteNonQueryAsync();
        }

        await using var videoCommand = new NpgsqlCommand(
            """
            INSERT INTO public.videos (
                id,
                platform,
                platform_video_url,
                platform_video_id,
                youtube_video_id,
                channel_id,
                author_original,
                title_original,
                description_original,
                video_url,
                created_at,
                updated_at
            )
            VALUES (
                @videoId,
                'youtube',
                @platformVideoUrl,
                @platformVideoId,
                @platformVideoId,
                @channelId,
                'Recent Search Test Author',
                'Recent Search Test Video',
                'Recent search test fixture video.',
                @videoUrl,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
            """,
            connection);

        videoCommand.Parameters.AddWithValue("videoId", videoId);
        videoCommand.Parameters.AddWithValue("channelId", channelId);
        videoCommand.Parameters.AddWithValue("platformVideoId", $"video-{videoId:N}");
        videoCommand.Parameters.AddWithValue("platformVideoUrl", $"https://youtube.com/watch?v=video-{videoId:N}");
        videoCommand.Parameters.AddWithValue("videoUrl", $"https://youtube.com/watch?v=video-{videoId:N}");
        await videoCommand.ExecuteNonQueryAsync();
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run", "--rm", "-d", "--name", _containerName,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL recent-search test container.");
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
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL recent-search test container to become ready.");
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

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EmbeddingGenerationResult("ollama", "test-model", 3, [0.1, 0.2, 0.3]));
        }
    }
}
