using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using StreamingDigest.Application;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresRecentSearchStore : IRecentSearchStore, IAsyncDisposable
{
    private const int DefaultInteractionBoostWindowDays = 90;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingService _embeddingService;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PostgresRecentSearchStore(string connectionString, IEmbeddingService embeddingService)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task<StoredRecentSearch> StoreSearchAsync(
        string query,
        SearchFilters filters,
        SearchUiSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedQuery = query.Trim();
        var searchedAt = DateTimeOffset.UtcNow;
        var filtersJson = SerializeFilters(filters);
        var contentHash = ComputeSha256(normalizedQuery);
        var embedding = await _embeddingService.GenerateEmbeddingAsync(normalizedQuery, cancellationToken);
        var vector = new Vector(embedding.Values.Select(static value => (float)value).ToArray());
        var recentSearchId = Guid.NewGuid();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await InsertRecentSearchAsync(
            connection,
            transaction,
            recentSearchId,
            normalizedQuery,
            searchedAt,
            settings,
            filtersJson,
            cancellationToken);

        await InsertSearchQueryEmbeddingAsync(
            connection,
            transaction,
            Guid.NewGuid(),
            recentSearchId,
            embedding,
            contentHash,
            vector,
            searchedAt,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new StoredRecentSearch(recentSearchId, normalizedQuery, searchedAt);
    }

    public async Task<IReadOnlyList<string>> ListRecentQueriesAsync(
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return [];
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT query_text
            FROM public.recent_searches
            ORDER BY searched_at DESC, id DESC
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("limit", Math.Max(take * 8, take));

        var recentQueries = new List<string>();
        var observedQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken) && recentQueries.Count < take)
        {
            var query = reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(query) || !observedQueries.Add(query))
            {
                continue;
            }

            recentQueries.Add(query);
        }

        recentQueries.Reverse();
        return recentQueries;
    }

    public async Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM public.recent_searches;", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordInteractionAsync(
        SearchInteractionEvent interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.ResultType);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.EventType);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.user_interaction_events (
                id,
                recent_search_id,
                video_id,
                search_document_id,
                result_type,
                event_type,
                activated_at,
                metadata_json
            )
            VALUES (
                @id,
                @recent_search_id,
                @video_id,
                @search_document_id,
                @result_type,
                @event_type,
                @activated_at,
                CAST(@metadata_json AS jsonb)
            );
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("recent_search_id", (object?)interaction.RecentSearchId ?? DBNull.Value);
        command.Parameters.AddWithValue("video_id", interaction.VideoId);
        command.Parameters.AddWithValue("search_document_id", (object?)interaction.SearchDocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("result_type", interaction.ResultType.Trim());
        command.Parameters.AddWithValue("event_type", interaction.EventType.Trim());
        command.Parameters.AddWithValue("activated_at", interaction.ActivatedAt);
        command.Parameters.AddWithValue("metadata_json", (object?)interaction.MetadataJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetRecentOpenCountsAsync(
        IEnumerable<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoIds);

        var requestedVideoIds = videoIds.Distinct().ToArray();
        if (requestedVideoIds.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var windowDays = await AppSettingReader.ReadIntAsync(
            connection,
            "search.interactionBoostWindowDays",
            DefaultInteractionBoostWindowDays,
            cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT video_id, COUNT(*)::int
            FROM public.user_interaction_events
            WHERE video_id = ANY(@video_ids)
              AND activated_at >= @cutoff
            GROUP BY video_id;
            """,
            connection);

        command.Parameters.AddWithValue("video_ids", requestedVideoIds);
        command.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow.AddDays(-windowDays));

        var counts = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetGuid(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static async Task InsertRecentSearchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recentSearchId,
        string query,
        DateTimeOffset searchedAt,
        SearchUiSettings settings,
        string? filtersJson,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.recent_searches (
                id,
                query_text,
                searched_at,
                text_weight,
                vector_weight,
                filters_json
            )
            VALUES (
                @id,
                @query_text,
                @searched_at,
                @text_weight,
                @vector_weight,
                CAST(@filters_json AS jsonb)
            );
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", recentSearchId);
        command.Parameters.AddWithValue("query_text", query);
        command.Parameters.AddWithValue("searched_at", searchedAt);
        command.Parameters.AddWithValue("text_weight", settings.TextWeight);
        command.Parameters.AddWithValue("vector_weight", settings.VectorWeight);
        command.Parameters.AddWithValue("filters_json", (object?)filtersJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSearchQueryEmbeddingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid embeddingId,
        Guid recentSearchId,
        EmbeddingGenerationResult embedding,
        string contentHash,
        Vector vector,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.search_query_embeddings (
                id,
                recent_search_id,
                provider,
                model,
                dimensions,
                content_hash,
                embedding,
                created_at
            )
            VALUES (
                @id,
                @recent_search_id,
                @provider,
                @model,
                @dimensions,
                @content_hash,
                @embedding,
                @created_at
            );
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", embeddingId);
        command.Parameters.AddWithValue("recent_search_id", recentSearchId);
        command.Parameters.AddWithValue("provider", embedding.Provider);
        command.Parameters.AddWithValue("model", embedding.Model);
        command.Parameters.AddWithValue("dimensions", embedding.Dimensions);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("embedding", vector);
        command.Parameters.AddWithValue("created_at", createdAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string? SerializeFilters(SearchFilters filters)
    {
        var hasAnyFilter =
            !string.IsNullOrWhiteSpace(filters.Channel) ||
            !string.IsNullOrWhiteSpace(filters.DateRange) ||
            !string.IsNullOrWhiteSpace(filters.ResultType) ||
            filters.HasTranscript.HasValue ||
            filters.HasRepo.HasValue ||
            filters.HasNotes.HasValue ||
            !string.IsNullOrWhiteSpace(filters.IngestionStatus);

        return hasAnyFilter
            ? JsonSerializer.Serialize(filters, _jsonSerializerOptions)
            : null;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
