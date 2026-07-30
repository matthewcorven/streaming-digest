using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class SearchDocumentEmbeddingStoreTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:pg17";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-embedding-store-tests-{Guid.NewGuid():N}";
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
    public async Task StoreAsync_persists_embedding_metadata_and_content_hash()
    {
        var generator = new SearchDocumentGenerator();
        var parentVideoId = Guid.NewGuid();
        var document = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            Notes =
            [
                new NoteDocumentInput(
                    SourceEntityId: Guid.NewGuid(),
                    MarkdownOriginal: "# Note\nA useful summary for embeddings.")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await InsertVideoAsync(parentVideoId);

        var stored = Assert.Single(await store.StoreAsync([document]));

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                sd.document_type,
                sd.source_entity_type,
                sd.content_hash,
                e.provider,
                e.model,
                e.dimensions,
                e.content_hash,
                e.source_text_hash,
                e.embedding_status
            FROM public.search_documents AS sd
            INNER JOIN public.embeddings AS e
                ON e.search_document_id = sd.id
            WHERE sd.id = @searchDocumentId;
            """,
            connection);
        command.Parameters.AddWithValue("searchDocumentId", stored.SearchDocumentId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(SearchDocumentTypeNames.Note, reader.GetString(0));
        Assert.Equal(SearchDocumentSourceEntityTypes.Note, reader.GetString(1));
        Assert.Equal(document.ContentHash, reader.GetString(2));
        Assert.Equal("ollama", reader.GetString(3));
        Assert.Equal("test-model", reader.GetString(4));
        Assert.Equal(3, reader.GetInt32(5));
        Assert.Equal(document.ContentHash, reader.GetString(6));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(7)));
        Assert.Equal("succeeded", reader.GetString(8));
    }

    [Fact]
    public async Task StoreAsync_persists_null_parent_video_for_non_video_documents()
    {
        var generator = new SearchDocumentGenerator();
        var document = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = null,
            RepositoryReadmeChunks =
            [
                new RepositoryReadmeChunkDocumentInput(
                    SourceEntityId: Guid.NewGuid(),
                    ContentOriginal: "# Repo\nRepository content without a parent video.")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        var stored = Assert.Single(await store.StoreAsync([document]));

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT parent_video_id IS NULL
            FROM public.search_documents
            WHERE id = @searchDocumentId;
            """,
            connection);
        command.Parameters.AddWithValue("searchDocumentId", stored.SearchDocumentId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
    }

    [Fact]
    public async Task StoreAsync_clears_stale_flag_when_reprocessing_existing_document()
    {
        var generator = new SearchDocumentGenerator();
        var document = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = null,
            Notes =
            [
                new NoteDocumentInput(
                    SourceEntityId: Guid.NewGuid(),
                    MarkdownOriginal: "# Note\nReprocess me")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        var firstStored = Assert.Single(await store.StoreAsync([document]));

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var updateCommand = new NpgsqlCommand(
            """
            UPDATE public.search_documents
            SET is_stale = TRUE
            WHERE id = @searchDocumentId;
            """,
            connection);
        updateCommand.Parameters.AddWithValue("searchDocumentId", firstStored.SearchDocumentId);
        await updateCommand.ExecuteNonQueryAsync();

        await store.StoreAsync([document]);

        await using var verifyCommand = new NpgsqlCommand(
            """
            SELECT is_stale
            FROM public.search_documents
            WHERE id = @searchDocumentId;
            """,
            connection);
        verifyCommand.Parameters.AddWithValue("searchDocumentId", firstStored.SearchDocumentId);

        await using var reader = await verifyCommand.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0));
    }

    [Fact]
    public async Task StoreAsync_removes_existing_document_when_content_becomes_empty()
    {
        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        var sourceEntityId = Guid.NewGuid();
        var initialDocument = new GeneratedSearchDocument(
            DocumentType: SearchDocumentTypeNames.Note,
            SourceEntityType: SearchDocumentSourceEntityTypes.Note,
            SourceEntityId: sourceEntityId,
            ParentVideoId: null,
            TitleEffective: "Initial title",
            BodyEffective: "Initial body",
            ContentHash: "initial-document-content",
            SourceFieldName: null,
            ChunkIndex: null);
        var emptyDocument = new GeneratedSearchDocument(
            DocumentType: SearchDocumentTypeNames.Note,
            SourceEntityType: SearchDocumentSourceEntityTypes.Note,
            SourceEntityId: sourceEntityId,
            ParentVideoId: null,
            TitleEffective: string.Empty,
            BodyEffective: string.Empty,
            ContentHash: "empty-document-content",
            SourceFieldName: null,
            ChunkIndex: null);

        await store.StoreAsync([initialDocument]);
        var stored = await store.StoreAsync([emptyDocument]);

        Assert.Empty(stored);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM public.search_documents
            WHERE source_entity_type = @sourceEntityType
              AND source_entity_id = @sourceEntityId;
            """,
            connection);
        command.Parameters.AddWithValue("sourceEntityType", SearchDocumentSourceEntityTypes.Note);
        command.Parameters.AddWithValue("sourceEntityId", sourceEntityId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
    }

    [Fact]
    public async Task StoreAsync_does_not_duplicate_unchanged_embeddings_when_rerun()
    {
        var generator = new SearchDocumentGenerator();
        var parentVideoId = Guid.NewGuid();
        var document = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            TranscriptChunks =
            [
                new TranscriptChunkDocumentInput(
                    SourceEntityId: Guid.NewGuid(),
                    TextOriginal: "Transcript content that should only produce one stored embedding.")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await InsertVideoAsync(parentVideoId);

        await store.StoreAsync([document]);
        await store.StoreAsync([document]);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM public.search_documents WHERE content_hash = @contentHash),
                (SELECT COUNT(*) FROM public.embeddings WHERE content_hash = @contentHash);
            """,
            connection);
        command.Parameters.AddWithValue("contentHash", document.ContentHash);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    [Fact]
    public async Task StoreAsync_replaces_existing_document_and_embedding_for_same_source_identity_when_content_changes()
    {
        var generator = new SearchDocumentGenerator();
        var parentVideoId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var initialDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            Notes =
            [
                new NoteDocumentInput(
                    SourceEntityId: sourceEntityId,
                    MarkdownOriginal: "# Note\nInitial content")
            ]
        }));

        var updatedDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = parentVideoId,
            Notes =
            [
                new NoteDocumentInput(
                    SourceEntityId: sourceEntityId,
                    MarkdownOriginal: "# Note\nUpdated content for regeneration")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await InsertVideoAsync(parentVideoId);

        var firstStored = Assert.Single(await store.StoreAsync([initialDocument]));
        var secondStored = Assert.Single(await store.StoreAsync([updatedDocument]));

        Assert.Equal(firstStored.SearchDocumentId, secondStored.SearchDocumentId);
        Assert.Equal(firstStored.EmbeddingId, secondStored.EmbeddingId);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) AS document_count,
                COUNT(DISTINCT e.id) AS embedding_count,
                MAX(sd.content_hash) AS content_hash
            FROM public.search_documents AS sd
            INNER JOIN public.embeddings AS e
                ON e.search_document_id = sd.id
            WHERE sd.source_entity_type = @sourceEntityType
              AND sd.source_entity_id = @sourceEntityId;
            """,
            connection);
        command.Parameters.AddWithValue("sourceEntityType", SearchDocumentSourceEntityTypes.Note);
        command.Parameters.AddWithValue("sourceEntityId", sourceEntityId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(updatedDocument.ContentHash, reader.GetString(2));
    }

    [Fact]
    public async Task StoreAsync_keeps_shared_canonical_documents_separate_per_parent_video()
    {
        var generator = new SearchDocumentGenerator();
        var repositoryId = Guid.NewGuid();
        var firstVideoId = Guid.NewGuid();
        var secondVideoId = Guid.NewGuid();

        await InsertVideoAsync(firstVideoId);
        await InsertVideoAsync(secondVideoId);

        var firstDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = firstVideoId,
            RepositoryReadmeChunks =
            [
                new RepositoryReadmeChunkDocumentInput(
                    SourceEntityId: repositoryId,
                    ContentOriginal: "# Repo\nShared canonical readme")
            ]
        }));

        var secondDocument = Assert.Single(generator.Generate(new SearchDocumentGenerationRequest
        {
            ParentVideoId = secondVideoId,
            RepositoryReadmeChunks =
            [
                new RepositoryReadmeChunkDocumentInput(
                    SourceEntityId: repositoryId,
                    ContentOriginal: "# Repo\nShared canonical readme")
            ]
        }));

        var store = new PostgresSearchDocumentEmbeddingStore(_connectionString!, new StubEmbeddingService());
        await store.StoreAsync([firstDocument, secondDocument]);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) AS document_count,
                COUNT(DISTINCT parent_video_id) AS parent_video_count,
                COUNT(DISTINCT e.search_document_id) AS embedding_document_count
            FROM public.search_documents AS sd
            INNER JOIN public.embeddings AS e
                ON e.search_document_id = sd.id
            WHERE sd.source_entity_type = @sourceEntityType
              AND sd.source_entity_id = @sourceEntityId
              AND sd.content_hash = @contentHash;
            """,
            connection);
        command.Parameters.AddWithValue("sourceEntityType", SearchDocumentSourceEntityTypes.Repository);
        command.Parameters.AddWithValue("sourceEntityId", repositoryId);
        command.Parameters.AddWithValue("contentHash", firstDocument.ContentHash);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));
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
            throw new InvalidOperationException("Docker did not return a container id for the embedding store test container.");
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
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL embedding store test container to become ready.");
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
                'Embedding Test Channel',
                'https://example.com/channel',
                'https://example.com/source',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
            """,
            connection))
        {
            channelCommand.Parameters.AddWithValue("channelId", channelId);
            channelCommand.Parameters.AddWithValue("youtubeChannelId", $"embedding-test-{channelId:N}");
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
                'Embedding Test Author',
                'Embedding Test Video',
                'Embedding store fixture video.',
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
}
