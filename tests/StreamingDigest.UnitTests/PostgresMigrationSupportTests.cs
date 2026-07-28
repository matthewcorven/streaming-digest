using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class PostgresMigrationSupportTests : IAsyncLifetime
{
    private const string ImageName = "postgres:16-alpine";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BaselineMigrationResourceName = "StreamingDigest.Infrastructure.Persistence.Postgres.Scripts.001_initial_baseline.sql";
    private const string LegacySearchIndexesAndViewsSql = """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                EXECUTE $exec1$
                    CREATE MATERIALIZED VIEW IF NOT EXISTS public.video_search_documents AS
                    SELECT
                        v.id,
                        v.title_original,
                        v.description_original,
                        to_tsvector('english', coalesce(v.title_original, '') || ' ' || coalesce(v.description_original, '')) AS search_vector,
                        NULL::vector(384) AS embedding_vector
                    FROM public.videos AS v;

                    CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
                    CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);

                    CREATE INDEX IF NOT EXISTS idx_video_search_documents_embedding_vector
                        ON public.video_search_documents
                        USING hnsw (embedding_vector vector_cosine_ops);

                    CREATE OR REPLACE FUNCTION public.search_videos(query_text text, query_vector vector(384) DEFAULT NULL::vector(384), limit_count integer DEFAULT 10)
                    RETURNS TABLE (
                        video_id uuid,
                        title text,
                        description text,
                        text_rank real,
                        vector_similarity double precision
                    )
                    LANGUAGE sql
                    AS $func1$
                        SELECT
                            s.id AS video_id,
                            s.title_original AS title,
                            s.description_original AS description,
                            ts_rank_cd(s.search_vector, websearch_to_tsquery('english', coalesce(query_text, ''))) AS text_rank,
                            0.0::double precision AS vector_similarity
                        FROM public.video_search_documents AS s
                        WHERE coalesce(query_text, '') = '' OR s.search_vector @@ websearch_to_tsquery('english', query_text)
                        ORDER BY text_rank DESC
                        LIMIT greatest(coalesce(limit_count, 10), 1);
                    $func1$;
                $exec1$;
            ELSE
                EXECUTE $exec2$
                    CREATE MATERIALIZED VIEW IF NOT EXISTS public.video_search_documents AS
                    SELECT
                        v.id,
                        v.title_original,
                        v.description_original,
                        to_tsvector('english', coalesce(v.title_original, '') || ' ' || coalesce(v.description_original, '')) AS search_vector,
                        NULL::text AS embedding_vector
                    FROM public.videos AS v;

                    CREATE UNIQUE INDEX IF NOT EXISTS idx_video_search_documents_id ON public.video_search_documents (id);
                    CREATE INDEX IF NOT EXISTS idx_video_search_documents_tsv ON public.video_search_documents USING gin (search_vector);

                    CREATE OR REPLACE FUNCTION public.search_videos(query_text text, query_vector text DEFAULT NULL::text, limit_count integer DEFAULT 10)
                    RETURNS TABLE (
                        video_id uuid,
                        title text,
                        description text,
                        text_rank real,
                        vector_similarity double precision
                    )
                    LANGUAGE sql
                    AS $func2$
                        SELECT
                            s.id AS video_id,
                            s.title_original AS title,
                            s.description_original AS description,
                            ts_rank_cd(s.search_vector, websearch_to_tsquery('english', coalesce(query_text, ''))) AS text_rank,
                            0.0::double precision AS vector_similarity
                        FROM public.video_search_documents AS s
                        WHERE coalesce(query_text, '') = '' OR s.search_vector @@ websearch_to_tsquery('english', query_text)
                        ORDER BY text_rank DESC
                        LIMIT greatest(coalesce(limit_count, 10), 1);
                    $func2$;
                $exec2$;
            END IF;
        END $$;

        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'pg_trgm') THEN
                CREATE INDEX IF NOT EXISTS idx_video_search_documents_title_trgm
                    ON public.video_search_documents
                    USING gin (coalesce(title_original, '') gin_trgm_ops);
            END IF;
        END $$;
        """;

    private readonly string _containerName = $"streaming-digest-support-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Raw_sql_migration_adds_search_objects_and_indexes()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
            SELECT
                EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = 'video_search_documents' AND c.relkind = 'm') AS has_view,
                EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public' AND p.proname = 'search_videos') AS has_search_function,
                EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = 'idx_video_search_documents_tsv' AND c.relkind = 'i') AS has_tsv_index,
                EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = 'idx_video_search_documents_search_text_trgm' AND c.relkind = 'i') AS has_trgm_index,
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') AS has_pg_trgm_extension,
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') AS has_unaccent_extension;
        ", connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
    }

    [Fact]
    public async Task Reapplying_current_search_migration_upgrades_existing_materialized_view_shape()
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await ApplyEmbeddedScriptAsync(connection, BaselineMigrationResourceName);
        await ApplySqlScriptAsync(connection, LegacySearchIndexesAndViewsSql);

        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        await using (var schemaCommand = new NpgsqlCommand(@"
            SELECT
                EXISTS (
                    SELECT 1
                    FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relname = 'video_search_documents'
                      AND c.relkind = 'm'
                      AND a.attname = 'search_text'
                      AND a.attnum > 0
                      AND NOT a.attisdropped
                ) AS has_search_text,
                EXISTS (
                    SELECT 1
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relname = 'idx_video_search_documents_search_text_trgm'
                      AND c.relkind = 'i'
                ) AS has_search_text_index;
        ", connection))
        await using (var schemaReader = await schemaCommand.ExecuteReaderAsync())
        {
            Assert.True(await schemaReader.ReadAsync());
            Assert.True(schemaReader.GetBoolean(0));
            Assert.True(schemaReader.GetBoolean(1));
        }

        var channelId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        await InsertChannelAsync(connection, channelId, "upgrade-channel", "Upgrade Channel");
        await InsertVideoAsync(
            connection,
            videoId,
            channelId,
            "upgrade-video",
            "Upgrade Search Document",
            "A useful body for the migration upgrade test.");

        await RefreshSearchDocumentsViewAsync(connection);

        var hasVectorExtension = await IsExtensionAvailableAsync(connection, "vector");
        var queryVectorLiteral = hasVectorExtension ? "NULL::vector(384)" : "NULL::text";

        await using var command = new NpgsqlCommand($@"
            SELECT title, description
            FROM public.search_videos(@queryText, {queryVectorLiteral}, 5);
        ", connection);
        command.Parameters.AddWithValue("queryText", "upgrad");

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Upgrade Search Document", reader.GetString(0));
        Assert.Equal("A useful body for the migration upgrade test.", reader.GetString(1));
    }

    [Fact]
    public async Task Search_videos_function_matches_partial_query_against_fixture_documents()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        var channelId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        await using (var insertChannel = new NpgsqlCommand(@"
            INSERT INTO public.channels (
                id,
                youtube_channel_id,
                name_original,
                profile_url,
                source_url,
                description_original,
                created_at,
                updated_at
            )
            VALUES (
                @channelId,
                'fixture-channel',
                'Fixture Channel',
                'https://example.com/fixture-channel',
                'https://example.com/fixture-source',
                'Fixture channel description',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection))
        {
            insertChannel.Parameters.AddWithValue("channelId", channelId);
            await insertChannel.ExecuteNonQueryAsync();
        }

        await using (var insertVideo = new NpgsqlCommand(@"
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
                'https://youtube.com/watch?v=fixture1',
                'fixture1',
                'fixture1',
                @channelId,
                'Fixture Author',
                'Fixture Search Document',
                'A useful body for the partial query test.',
                'https://youtube.com/watch?v=fixture1',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection))
        {
            insertVideo.Parameters.AddWithValue("videoId", videoId);
            insertVideo.Parameters.AddWithValue("channelId", channelId);
            await insertVideo.ExecuteNonQueryAsync();
        }

        await using var refreshView = new NpgsqlCommand("REFRESH MATERIALIZED VIEW public.video_search_documents;", connection);
        await refreshView.ExecuteNonQueryAsync();

        var hasVectorExtension = await IsExtensionAvailableAsync(connection, "vector");
        var queryVectorLiteral = hasVectorExtension ? "NULL::vector(384)" : "NULL::text";

        await using var command = new NpgsqlCommand($@"
            SELECT title, description FROM public.search_videos(@query_text, {queryVectorLiteral}, 5)
        ", connection);
        command.Parameters.AddWithValue("query_text", "fixtur");
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("Fixture Search Document", reader.GetString(0));
        Assert.Equal("A useful body for the partial query test.", reader.GetString(1));
    }

    [Fact]
    public async Task Search_videos_supports_trigram_fallback_for_partial_queries_while_preserving_full_text_matches()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        var channelId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        await using (var insertChannel = new NpgsqlCommand(@"
            INSERT INTO public.channels (
                id,
                youtube_channel_id,
                name_original,
                profile_url,
                source_url,
                description_original,
                created_at,
                updated_at
            )
            VALUES (
                @channelId,
                'channel-1',
                'Example Channel',
                'https://example.com/channel',
                'https://example.com/source',
                'Example channel description',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection))
        {
            insertChannel.Parameters.AddWithValue("channelId", channelId);
            await insertChannel.ExecuteNonQueryAsync();
        }

        await using (var insertVideo = new NpgsqlCommand(@"
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
                'https://youtube.com/watch?v=abc123',
                'abc123',
                'abc123',
                @channelId,
                'Example Author',
                'Space Exploration: The Future',
                'A documentary about the next chapter of human exploration.',
                'https://youtube.com/watch?v=abc123',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection))
        {
            insertVideo.Parameters.AddWithValue("videoId", videoId);
            insertVideo.Parameters.AddWithValue("channelId", channelId);
            await insertVideo.ExecuteNonQueryAsync();
        }

        await using var refreshView = new NpgsqlCommand("REFRESH MATERIALIZED VIEW public.video_search_documents;", connection);
        await refreshView.ExecuteNonQueryAsync();

        var hasVectorExtension = await IsExtensionAvailableAsync(connection, "vector");
        var queryVectorLiteral = hasVectorExtension ? "NULL::vector(384)" : "NULL::text";

        await using var partialQuery = new NpgsqlCommand($@"
            SELECT title
            FROM public.search_videos(@queryText, {queryVectorLiteral}, @limitCount);
        ", connection);
        partialQuery.Parameters.AddWithValue("queryText", "explorat");
        partialQuery.Parameters.AddWithValue("limitCount", 10);

        await using var partialReader = await partialQuery.ExecuteReaderAsync();
        Assert.True(await partialReader.ReadAsync());
        Assert.Equal("Space Exploration: The Future", partialReader.GetString(0));
        Assert.False(await partialReader.ReadAsync());
        await partialReader.CloseAsync();

        await using var fullTextQuery = new NpgsqlCommand($@"
            SELECT title
            FROM public.search_videos(@queryText, {queryVectorLiteral}, @limitCount);
        ", connection);
        fullTextQuery.Parameters.AddWithValue("queryText", "space exploration");
        fullTextQuery.Parameters.AddWithValue("limitCount", 10);

        await using var fullTextReader = await fullTextQuery.ExecuteReaderAsync();
        Assert.True(await fullTextReader.ReadAsync());
        Assert.Equal("Space Exploration: The Future", fullTextReader.GetString(0));
        Assert.False(await fullTextReader.ReadAsync());
    }

    private static async Task<bool> IsExtensionAvailableAsync(NpgsqlConnection connection, string extensionName)
    {
        await using var command = new NpgsqlCommand(@"
            SELECT EXISTS (
                SELECT 1
                FROM pg_available_extensions
                WHERE name = @extensionName
            );
        ", connection);
        command.Parameters.AddWithValue("extensionName", extensionName);
        var result = await command.ExecuteScalarAsync();
        return result is bool available && available;
    }

    private static async Task ApplyEmbeddedScriptAsync(NpgsqlConnection connection, string resourceName)
    {
        var assembly = typeof(PostgresMigrationRunner).Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' could not be loaded.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        await ApplySqlScriptAsync(connection, script);
    }

    private static async Task ApplySqlScriptAsync(NpgsqlConnection connection, string script)
    {
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertChannelAsync(NpgsqlConnection connection, Guid channelId, string youtubeChannelId, string channelName)
    {
        await using var insertChannel = new NpgsqlCommand(@"
            INSERT INTO public.channels (
                id,
                youtube_channel_id,
                name_original,
                profile_url,
                source_url,
                description_original,
                created_at,
                updated_at
            )
            VALUES (
                @channelId,
                @youtubeChannelId,
                @channelName,
                'https://example.com/channel',
                'https://example.com/source',
                'Example channel description',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection);
        insertChannel.Parameters.AddWithValue("channelId", channelId);
        insertChannel.Parameters.AddWithValue("youtubeChannelId", youtubeChannelId);
        insertChannel.Parameters.AddWithValue("channelName", channelName);
        await insertChannel.ExecuteNonQueryAsync();
    }

    private static async Task InsertVideoAsync(
        NpgsqlConnection connection,
        Guid videoId,
        Guid channelId,
        string videoIdentifier,
        string title,
        string description)
    {
        await using var insertVideo = new NpgsqlCommand(@"
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
                @videoIdentifier,
                @videoIdentifier,
                @channelId,
                'Example Author',
                @title,
                @description,
                @platformVideoUrl,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            );
        ", connection);
        insertVideo.Parameters.AddWithValue("videoId", videoId);
        insertVideo.Parameters.AddWithValue("channelId", channelId);
        insertVideo.Parameters.AddWithValue("videoIdentifier", videoIdentifier);
        insertVideo.Parameters.AddWithValue("platformVideoUrl", $"https://youtube.com/watch?v={videoIdentifier}");
        insertVideo.Parameters.AddWithValue("title", title);
        insertVideo.Parameters.AddWithValue("description", description);
        await insertVideo.ExecuteNonQueryAsync();
    }

    private static async Task RefreshSearchDocumentsViewAsync(NpgsqlConnection connection)
    {
        await using var refreshView = new NpgsqlCommand("REFRESH MATERIALIZED VIEW public.video_search_documents;", connection);
        await refreshView.ExecuteNonQueryAsync();
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL search migration test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_containerName))
        {
            return;
        }

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

        throw new TimeoutException("Timed out waiting for the PostgreSQL search migration test container to become ready.");
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
}
