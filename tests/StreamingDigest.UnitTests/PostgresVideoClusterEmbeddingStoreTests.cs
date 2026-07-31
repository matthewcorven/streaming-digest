using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class PostgresVideoClusterEmbeddingStoreTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:pg17";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-cluster-embedding-tests-{Guid.NewGuid():N}";
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
    public async Task BuildForVideoAsync_creates_cluster_embedding_after_document_embeddings_exist()
    {
        var generator = new SearchDocumentGenerator();
        var videoId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var transcriptCueId = Guid.NewGuid();

        await InsertVideoAsync(videoId);

        var documents = generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = videoId,
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(videoId, "Clustered title", DescriptionOriginal: "Clustered description")
            ],
            TranscriptChunks =
            [
                new TranscriptChunkDocumentInput(transcriptCueId, "Transcript content for the aggregate embedding.", ChunkIndex: 0)
            ],
            Notes =
            [
                new NoteDocumentInput(noteId, "# Note\nA useful note for clustering.")
            ]
        });

        var documentStore = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await documentStore.StoreAsync(documents);

        var clusterStore = new PostgresVideoClusterEmbeddingStore(_connectionString!);
        var built = await clusterStore.BuildForVideoAsync(videoId);

        var cluster = Assert.Single(built);
        Assert.Equal(videoId, cluster.VideoId);
        Assert.Equal("ollama", cluster.Provider);
        Assert.Equal("test-model", cluster.Model);
        Assert.Equal(3, cluster.Dimensions);
        Assert.False(cluster.IsStale);
        Assert.Contains(noteId.ToString(), cluster.ComponentWeightsJson, StringComparison.OrdinalIgnoreCase);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*), BOOL_AND(is_stale = FALSE)
            FROM public.video_cluster_embeddings
            WHERE video_id = @video_id;
            """,
            connection);
        command.Parameters.AddWithValue("video_id", videoId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
    }

    [Fact]
    public async Task StoreAsync_marks_only_the_affected_parent_cluster_stale_for_note_title_and_transcript_updates()
    {
        var generator = new SearchDocumentGenerator();
        var firstVideoId = Guid.NewGuid();
        var secondVideoId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var transcriptCueId = Guid.NewGuid();

        await InsertVideoAsync(firstVideoId);
        await InsertVideoAsync(secondVideoId);

        var firstVideoDocuments = generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = firstVideoId,
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(firstVideoId, "Original title", DescriptionOriginal: "Original description")
            ],
            TranscriptChunks =
            [
                new TranscriptChunkDocumentInput(transcriptCueId, "Original transcript", ChunkIndex: 0)
            ],
            Notes =
            [
                new NoteDocumentInput(noteId, "# Note\nOriginal note")
            ]
        });

        var secondVideoDocuments = generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = secondVideoId,
            VideoMetadata =
            [
                new VideoMetadataDocumentInput(secondVideoId, "Unaffected title", DescriptionOriginal: "Unaffected description")
            ]
        });

        var documentStore = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await documentStore.StoreAsync(firstVideoDocuments.Concat(secondVideoDocuments));

        var clusterStore = new PostgresVideoClusterEmbeddingStore(_connectionString!);
        await clusterStore.BuildForVideoAsync(firstVideoId);
        await clusterStore.BuildForVideoAsync(secondVideoId);

        var updatedNoteDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = firstVideoId,
            Notes = [new NoteDocumentInput(noteId, "# Note\nUpdated note")]
        }));

        await documentStore.StoreAsync([updatedNoteDocument]);
        await AssertClusterStaleStateAsync(firstVideoId, expectedFirstVideoIsStale: true, secondVideoId, expectedSecondVideoIsStale: false);

        await clusterStore.BuildForVideoAsync(firstVideoId);

        var updatedTitleDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = firstVideoId,
            VideoMetadata = [new VideoMetadataDocumentInput(firstVideoId, "Updated title", DescriptionOriginal: "Original description")]
        }));

        await documentStore.StoreAsync([updatedTitleDocument]);
        await AssertClusterStaleStateAsync(firstVideoId, expectedFirstVideoIsStale: true, secondVideoId, expectedSecondVideoIsStale: false);

        await clusterStore.BuildForVideoAsync(firstVideoId);

        var updatedTranscriptDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = firstVideoId,
            TranscriptChunks = [new TranscriptChunkDocumentInput(transcriptCueId, "Updated transcript", ChunkIndex: 0)]
        }));

        await documentStore.StoreAsync([updatedTranscriptDocument]);
        await AssertClusterStaleStateAsync(firstVideoId, expectedFirstVideoIsStale: true, secondVideoId, expectedSecondVideoIsStale: false);
    }

    [Fact]
    public async Task GetHighSignalMatchesAsync_ignores_provider_model_and_dimension_mismatches()
    {
        var generator = new SearchDocumentGenerator();
        var videoId = Guid.NewGuid();
        await InsertVideoAsync(videoId);

        var documents = generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = videoId,
            Notes =
            [
                new NoteDocumentInput(Guid.NewGuid(), "# Note\nHigh-signal cluster match")
            ]
        });

        var documentStore = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await documentStore.StoreAsync(documents);

        var clusterStore = new PostgresVideoClusterEmbeddingStore(_connectionString!);
        await clusterStore.BuildForVideoAsync(videoId);

        var matchingRecentSearchStore = new PostgresRecentSearchStore(_connectionString!, new StubEmbeddingService());
        var matchingSearch = await matchingRecentSearchStore.StoreSearchAsync("high signal", SearchFilters.Empty, SearchUiSettings.Default);

        var mismatchedProviderStore = new PostgresRecentSearchStore(_connectionString!, new FixedEmbeddingService("openai", "test-model", [0.1, 0.2, 0.3]));
        var mismatchedProviderSearch = await mismatchedProviderStore.StoreSearchAsync("provider mismatch", SearchFilters.Empty, SearchUiSettings.Default);

        var mismatchedModelStore = new PostgresRecentSearchStore(_connectionString!, new FixedEmbeddingService("ollama", "other-model", [0.1, 0.2, 0.3]));
        var mismatchedModelSearch = await mismatchedModelStore.StoreSearchAsync("model mismatch", SearchFilters.Empty, SearchUiSettings.Default);

        var mismatchedDimensionStore = new PostgresRecentSearchStore(_connectionString!, new FixedEmbeddingService("ollama", "test-model", [0.1, 0.2, 0.3, 0.4]));
        var mismatchedDimensionSearch = await mismatchedDimensionStore.StoreSearchAsync("dimension mismatch", SearchFilters.Empty, SearchUiSettings.Default);

        var matches = await clusterStore.GetHighSignalMatchesAsync(videoId, 70d);

        Assert.Contains(matches, match => match.RecentSearchId == matchingSearch.Id);
        Assert.DoesNotContain(matches, match => match.RecentSearchId == mismatchedProviderSearch.Id);
        Assert.DoesNotContain(matches, match => match.RecentSearchId == mismatchedModelSearch.Id);
        Assert.DoesNotContain(matches, match => match.RecentSearchId == mismatchedDimensionSearch.Id);
    }

    private async Task AssertClusterStaleStateAsync(
        Guid firstVideoId,
        bool expectedFirstVideoIsStale,
        Guid secondVideoId,
        bool expectedSecondVideoIsStale)
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT video_id, is_stale
            FROM public.video_cluster_embeddings
            WHERE video_id = ANY(@video_ids)
            ORDER BY video_id;
            """,
            connection);
        command.Parameters.AddWithValue("video_ids", new[] { firstVideoId, secondVideoId });

        var states = new Dictionary<Guid, bool>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states[reader.GetGuid(0)] = reader.GetBoolean(1);
        }

        Assert.Equal(expectedFirstVideoIsStale, states[firstVideoId]);
        Assert.Equal(expectedSecondVideoIsStale, states[secondVideoId]);
    }

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync("docker", new[]
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
        });

        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the video-cluster embedding test container.");
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
        for (var attempt = 0; attempt < 60; attempt++)
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL video-cluster embedding test container to become ready.");
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
                @id,
                @youtube_channel_id,
                @name_original,
                @profile_url,
                @source_url,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
            """,
            connection))
        {
            channelCommand.Parameters.AddWithValue("id", channelId);
            channelCommand.Parameters.AddWithValue("youtube_channel_id", $"channel-{channelId:N}");
            channelCommand.Parameters.AddWithValue("name_original", "Cluster Test Channel");
            channelCommand.Parameters.AddWithValue("profile_url", $"https://youtube.com/channel/{channelId:N}");
            channelCommand.Parameters.AddWithValue("source_url", $"https://youtube.com/channel/{channelId:N}");
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
                ingestion_status,
                transcript_status,
                screenshot_status,
                is_long_form,
                created_at,
                updated_at
            )
            VALUES (
                @id,
                'youtube',
                @platform_video_url,
                @platform_video_id,
                @youtube_video_id,
                @channel_id,
                'Cluster Test Author',
                'Cluster Test Video',
                'Cluster test description',
                @video_url,
                'processed',
                'processed',
                'unknown',
                TRUE,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
            """,
            connection);
        videoCommand.Parameters.AddWithValue("id", videoId);
        videoCommand.Parameters.AddWithValue("platform_video_url", $"https://youtube.com/watch?v={videoId:N}");
        videoCommand.Parameters.AddWithValue("platform_video_id", videoId.ToString("N"));
        videoCommand.Parameters.AddWithValue("youtube_video_id", videoId.ToString("N"));
        videoCommand.Parameters.AddWithValue("channel_id", channelId);
        videoCommand.Parameters.AddWithValue("video_url", $"https://youtube.com/watch?v={videoId:N}");
        await videoCommand.ExecuteNonQueryAsync();
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmbeddingGenerationResult("ollama", "test-model", 3, [0.1, 0.2, 0.3]));
    }

    private sealed class FixedEmbeddingService(string provider, string model, IReadOnlyList<double> values) : IEmbeddingService
    {
        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmbeddingGenerationResult(provider, model, values.Count, values));
    }
}
