using System.Globalization;
using System.Text;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using StreamingDigest.Application;

namespace StreamingDigest.Infrastructure.Persistence;

/// <summary>
/// DB-backed hybrid search over <c>search_documents</c> + <c>embeddings</c>. Aggregates
/// matching documents into one <see cref="SearchCorpusCluster"/> per video so the pure
/// <see cref="HybridRankingService"/> can perform the final blend. Text relevance comes
/// from the generated <c>fts_body</c> tsvector column (migration019); vector relevance
/// comes from pgvector cosine distance (<c><=></c>) against the query embedding.
/// </summary>
public sealed class PostgresSearchCorpusSearcher : ISearchCorpusSearcher, IAsyncDisposable
{
    private const int DefaultMaxClusters = 25;
    private const int DefaultMaxDocumentsPerCluster = 8;
    private const string VideoResultType = "video";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresSearchCorpusSearcher(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task<SearchCorpusReadiness> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM public.search_documents AS sd
            INNER JOIN public.embeddings AS e
                ON e.search_document_id = sd.id
            WHERE sd.is_stale = FALSE
              AND e.embedding_status = 'succeeded';
            """,
            connection);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        var hasCorpus = count > 0L;
        return new SearchCorpusReadiness(hasCorpus, count);
    }

    public Task<IReadOnlyList<SearchCorpusCluster>> SearchAsync(
        SearchCorpusSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SearchInternalAsync(request, cancellationToken);
    }

    private async Task<IReadOnlyList<SearchCorpusCluster>> SearchInternalAsync(
        SearchCorpusSearchRequest request,
        CancellationToken cancellationToken)
    {
        // Result type filter: every cluster is a video cluster, so non-video result types
        // (e.g. "audio") have no matching documents. Return empty rather than fabricating.
        if (!string.IsNullOrWhiteSpace(request.Filters.ResultType)
            && !string.Equals(request.Filters.ResultType, VideoResultType, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<SearchCorpusCluster>();
        }

        var maxClusters = Math.Max(1, request.MaxClusters <= 0 ? DefaultMaxClusters : request.MaxClusters);
        var maxDocumentsPerCluster = Math.Max(1, request.MaxDocumentsPerCluster <= 0 ? DefaultMaxDocumentsPerCluster : request.MaxDocumentsPerCluster);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // The text leg uses websearch_to_tsquery against the generated fts_body column.
        // The vector leg joins the document embeddings to the query embedding by
        // provider/model/dimensions and ranks by cosine similarity. We FULL OUTER JOIN both
        // legs by search_document_id so a document that matches only one leg (text-only or
        // vector-only) still surfaces — semantic recall from the vector leg is not gated on
        // a text match. For each document we take the max text rank and max vector
        // similarity observed, then group per video.
        var hasVectorLeg = request.QueryEmbedding is { Count: > 0 }
            && !string.IsNullOrWhiteSpace(request.QueryEmbeddingProvider)
            && !string.IsNullOrWhiteSpace(request.QueryEmbeddingModel)
            && request.QueryEmbeddingDimensions is > 0;

        var sql = BuildSearchSql(request, hasVectorLeg);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("query", request.Query.Trim());
        command.Parameters.AddWithValue("max_clusters", maxClusters);
        command.Parameters.AddWithValue("max_documents_per_cluster", maxDocumentsPerCluster);

        if (request.QueryEmbedding is { Count: > 0 } embedding
            && !string.IsNullOrWhiteSpace(request.QueryEmbeddingProvider)
            && !string.IsNullOrWhiteSpace(request.QueryEmbeddingModel)
            && request.QueryEmbeddingDimensions is > 0)
        {
            command.Parameters.AddWithValue("query_embedding", new Vector(embedding.Select(static value => (float)value).ToArray()));
            command.Parameters.AddWithValue("query_provider", request.QueryEmbeddingProvider!);
            command.Parameters.AddWithValue("query_model", request.QueryEmbeddingModel!);
            command.Parameters.AddWithValue("query_dimensions", request.QueryEmbeddingDimensions!.Value);
        }

        AppendFilterParameters(command, request.Filters);

        var documentsByVideo = new Dictionary<Guid, List<SearchCorpusDocument>>();
        var videoMeta = new Dictionary<Guid, VideoRow>();
        var recentOpenCounts = await ReadRecentOpenCountsAsync(connection, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var videoId = reader.GetGuid(0);
            var title = reader.GetString(1);
            var channel = reader.GetString(2);
            var publishDate = reader.IsDBNull(3) ? null : (DateTimeOffset?)reader.GetFieldValue<DateTimeOffset>(3);
            var transcriptStatus = reader.GetString(4);
            var screenshotStatus = reader.GetString(5);
            var ingestionStatus = reader.GetString(6);
            var hasRepo = reader.GetBoolean(7);
            var hasNotes = reader.GetBoolean(8);
            var documentId = reader.GetGuid(9);
            var documentType = reader.GetString(10);
            var textRank = reader.GetDouble(11);
            var vectorSimilarity = reader.GetDouble(12);
            var snippet = reader.IsDBNull(13) ? null : reader.GetString(13);
            var matchedFields = reader.IsDBNull(14) ? Array.Empty<string>() : reader.GetString(14).Split('|', StringSplitOptions.RemoveEmptyEntries);

            if (!videoMeta.ContainsKey(videoId))
            {
                videoMeta[videoId] = new VideoRow(
                    videoId,
                    title,
                    channel,
                    publishDate,
                    transcriptStatus,
                    screenshotStatus,
                    ingestionStatus,
                    hasRepo,
                    hasNotes);
            }

            if (!documentsByVideo.TryGetValue(videoId, out var documents))
            {
                documents = new List<SearchCorpusDocument>();
                documentsByVideo[videoId] = documents;
            }

            // ts_rank_cd is unbounded; clamp into [0,1] for the ranking layer.
            var normalizedTextScore = ClampScore(textRank);
            // cosine similarity is already in [0,1] for unit-ish embeddings; clamp for safety.
            var normalizedVectorScore = ClampScore(vectorSimilarity);

            documents.Add(new SearchCorpusDocument(
                documentId,
                documentType,
                normalizedTextScore,
                normalizedVectorScore,
                snippet,
                matchedFields));
        }

        var clusters = new List<SearchCorpusCluster>(documentsByVideo.Count);
        foreach (var (videoId, documents) in documentsByVideo)
        {
            var meta = videoMeta[videoId];
            var topDocuments = documents
                .OrderByDescending(document => Math.Max(document.TextScore, document.VectorScore))
                .Take(maxDocumentsPerCluster)
                .ToList();

            var hasMatchingNote = topDocuments.Any(document => string.Equals(document.DocumentType, "note", StringComparison.OrdinalIgnoreCase));
            var recentOpenCount = recentOpenCounts.TryGetValue(videoId, out var openCount) ? openCount : 0;

            clusters.Add(new SearchCorpusCluster(
                VideoId: videoId,
                ClusterId: videoId.ToString("D", CultureInfo.InvariantCulture),
                Title: meta.Title,
                Channel: meta.Channel,
                PublishDate: meta.PublishDate,
                ResultType: VideoResultType,
                HasTranscript: string.Equals(meta.TranscriptStatus, "processed", StringComparison.OrdinalIgnoreCase),
                HasRepo: meta.HasRepo,
                HasNotes: meta.HasNotes,
                HasScreenshot: !string.Equals(meta.ScreenshotStatus, "unknown", StringComparison.OrdinalIgnoreCase)
                              && !string.Equals(meta.ScreenshotStatus, "missing", StringComparison.OrdinalIgnoreCase),
                ProcessingStatus: meta.IngestionStatus,
                CanRetry: string.Equals(meta.IngestionStatus, "failed", StringComparison.OrdinalIgnoreCase),
                Documents: topDocuments,
                RecentOpenCount: recentOpenCount,
                HasMatchingNote: hasMatchingNote));
        }

        return clusters
            .OrderByDescending(cluster => cluster.Documents.Max(document => Math.Max(document.TextScore, document.VectorScore)))
            .Take(maxClusters)
            .ToList();
    }

    private static string BuildSearchSql(SearchCorpusSearchRequest request, bool hasVectorLeg)
    {
        var filterWhere = BuildFilterClauses(request.Filters).Where;

        var vectorCte = hasVectorLeg
            ? """
            vector_matches AS (
                SELECT
                    sd.id AS search_document_id,
                    sd.parent_video_id,
                    MAX(1 - (e.embedding <=> @query_embedding)) AS vector_similarity
                FROM public.search_documents AS sd
                INNER JOIN public.embeddings AS e
                    ON e.search_document_id = sd.id
                   AND e.embedding_status = 'succeeded'
                   AND e.provider = @query_provider
                   AND e.model = @query_model
                   AND e.dimensions = @query_dimensions
                   AND vector_dims(e.embedding) = @query_dimensions
                WHERE sd.is_stale = FALSE
                  AND sd.parent_video_id IS NOT NULL
                GROUP BY sd.id, sd.parent_video_id
            ),
            """
            : string.Empty;

        var docScoresCte = hasVectorLeg
            ? """
            doc_scores AS (
                SELECT
                    COALESCE(tm.search_document_id, vm.search_document_id) AS search_document_id,
                    COALESCE(tm.parent_video_id, vm.parent_video_id) AS parent_video_id,
                    COALESCE(MAX(tm.text_rank), 0) AS text_rank,
                    COALESCE(MAX(vm.vector_similarity), 0) AS vector_similarity
                FROM text_matches AS tm
                FULL OUTER JOIN vector_matches AS vm
                    ON vm.search_document_id = tm.search_document_id
                GROUP BY 1, 2
            )
            """
            : """
            doc_scores AS (
                SELECT
                    tm.search_document_id,
                    tm.parent_video_id,
                    MAX(tm.text_rank) AS text_rank,
                    0 AS vector_similarity
                FROM text_matches AS tm
                GROUP BY tm.search_document_id, tm.parent_video_id
            )
            """;

        return $"""
            WITH text_matches AS (
                SELECT
                    sd.id AS search_document_id,
                    sd.parent_video_id,
                    ts_rank_cd(sd.fts_body, websearch_to_tsquery('english', @query)) AS text_rank
                FROM public.search_documents AS sd
                WHERE sd.fts_body @@ websearch_to_tsquery('english', @query)
                  AND sd.is_stale = FALSE
                  AND sd.parent_video_id IS NOT NULL
            ),
            {vectorCte}
            {docScoresCte}
            SELECT
                v.id AS video_id,
                COALESCE(v.title_override, v.title_original) AS title,
                COALESCE(c.name_override, c.name_original) AS channel,
                v.published_at,
                v.transcript_status,
                v.screenshot_status,
                v.ingestion_status,
                EXISTS (
                    SELECT 1 FROM public.search_documents repo
                    WHERE repo.parent_video_id = v.id
                      AND repo.document_type = 'repository_readme_chunk'
                      AND repo.is_stale = FALSE
                ) AS has_repo,
                EXISTS (
                    SELECT 1 FROM public.search_documents note
                    WHERE note.parent_video_id = v.id
                      AND note.document_type = 'note'
                      AND note.is_stale = FALSE
                ) AS has_notes,
                ds.search_document_id,
                sd.document_type,
                ds.text_rank,
                ds.vector_similarity,
                ts_headline('english', COALESCE(sd.title_effective, '') || ' ' || COALESCE(sd.body_effective, ''),
                            websearch_to_tsquery('english', @query),
                            'MaxWords=35, MinWords=10, ShortWord=3') AS snippet,
                CASE
                    WHEN ds.text_rank > 0 AND ds.vector_similarity > 0 THEN 'title,body,semantic'
                    WHEN ds.text_rank > 0 THEN 'title,body'
                    WHEN ds.vector_similarity > 0 THEN 'semantic'
                    ELSE ''
                END AS matched_fields
            FROM doc_scores AS ds
            INNER JOIN public.search_documents AS sd ON sd.id = ds.search_document_id
            INNER JOIN public.videos AS v ON v.id = ds.parent_video_id
            INNER JOIN public.channels AS c ON c.id = v.channel_id
            WHERE 1 = 1
            {filterWhere}
            ORDER BY GREATEST(ds.text_rank, ds.vector_similarity) DESC, v.id
            LIMIT @max_clusters * @max_documents_per_cluster;
            """;
    }

    private static FilterClauses BuildFilterClauses(SearchFilters filters)
    {
        var where = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(filters.Channel))
        {
            where.Append(" AND COALESCE(c.name_override, c.name_original) = ANY(@channels)");
        }

        if (filters.HasTranscript is true)
        {
            where.Append(" AND v.transcript_status = 'processed'");
        }
        else if (filters.HasTranscript is false)
        {
            where.Append(" AND v.transcript_status <> 'processed'");
        }

        if (filters.HasRepo is true)
        {
            where.Append(" AND EXISTS (SELECT 1 FROM public.search_documents repo WHERE repo.parent_video_id = v.id AND repo.document_type = 'repository_readme_chunk' AND repo.is_stale = FALSE)");
        }

        if (filters.HasNotes is true)
        {
            where.Append(" AND EXISTS (SELECT 1 FROM public.search_documents note WHERE note.parent_video_id = v.id AND note.document_type = 'note' AND note.is_stale = FALSE)");
        }

        if (!string.IsNullOrWhiteSpace(filters.IngestionStatus))
        {
            where.Append(" AND v.ingestion_status = @ingestion_status");
        }

        if (!string.IsNullOrWhiteSpace(filters.DateRange))
        {
            where.Append(" AND (CASE WHEN @date_range = 'last-week' THEN v.published_at >= CURRENT_TIMESTAMP - INTERVAL '7 days' WHEN @date_range = 'last-month' THEN v.published_at >= CURRENT_TIMESTAMP - INTERVAL '30 days' WHEN @date_range = 'older' THEN v.published_at < CURRENT_TIMESTAMP - INTERVAL '30 days' ELSE TRUE END)");
        }

        return new FilterClauses(where.ToString());
    }

    private static void AppendFilterParameters(NpgsqlCommand command, SearchFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Channel))
        {
            command.Parameters.AddWithValue("channels", filters.Channel.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (!string.IsNullOrWhiteSpace(filters.IngestionStatus))
        {
            command.Parameters.AddWithValue("ingestion_status", filters.IngestionStatus.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filters.DateRange))
        {
            command.Parameters.AddWithValue("date_range", filters.DateRange.Trim());
        }
    }

    private async Task<IReadOnlyDictionary<Guid, int>> ReadRecentOpenCountsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<Guid, int>();
        await using var command = new NpgsqlCommand(
            """
            SELECT video_id, COUNT(*)::int
            FROM public.user_interaction_events
            WHERE activated_at >= CURRENT_TIMESTAMP - INTERVAL '90 days'
            GROUP BY video_id;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetGuid(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    private static double ClampScore(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            return 0.0;
        }

        return value > 1.0 ? 1.0 : value;
    }

    private sealed record VideoRow(
        Guid VideoId,
        string Title,
        string Channel,
        DateTimeOffset? PublishDate,
        string TranscriptStatus,
        string ScreenshotStatus,
        string IngestionStatus,
        bool HasRepo,
        bool HasNotes);

    private sealed record FilterClauses(string Where);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
