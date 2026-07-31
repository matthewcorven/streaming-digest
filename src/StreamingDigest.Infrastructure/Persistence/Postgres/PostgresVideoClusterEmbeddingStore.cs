using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using StreamingDigest.Application;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresVideoClusterEmbeddingStore : IVideoClusterEmbeddingStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;

    public PostgresVideoClusterEmbeddingStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task<IReadOnlyList<StoredVideoClusterEmbedding>> BuildForVideoAsync(
        Guid videoId,
        Guid? generatedByOperationId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var components = await LoadComponentsAsync(connection, transaction, videoId, cancellationToken);
        var groups = components
            .GroupBy(component => new EmbeddingSpace(component.Provider, component.Model, component.Dimensions))
            .ToList();

        if (groups.Count == 0)
        {
            await MarkAllVideoRowsStaleAsync(connection, transaction, videoId, generatedByOperationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var builtEmbeddings = new List<StoredVideoClusterEmbedding>(groups.Count);
        foreach (var group in groups)
        {
            var aggregateVector = BuildAggregateVector(group.ToList(), out var normalizedComponents);
            if (aggregateVector is null)
            {
                continue;
            }

            var contentHash = ComputeClusterContentHash(group.Key, normalizedComponents);
            var componentWeightsJson = JsonSerializer.Serialize(
                normalizedComponents.Select(static component => new
                {
                    component.SearchDocumentId,
                    component.EmbeddingId,
                    component.DocumentType,
                    component.SourceEntityType,
                    component.SourceEntityId,
                    component.ContentHash,
                    component.Weight
                }),
                JsonOptions);

            builtEmbeddings.Add(await UpsertClusterEmbeddingAsync(
                connection,
                transaction,
                videoId,
                group.Key,
                contentHash,
                aggregateVector,
                componentWeightsJson,
                generatedByOperationId,
                cancellationToken));
        }

        await MarkMissingVideoRowsStaleAsync(
            connection,
            transaction,
            videoId,
            builtEmbeddings.Select(row => new EmbeddingSpace(row.Provider, row.Model, row.Dimensions)).ToArray(),
            generatedByOperationId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return builtEmbeddings;
    }

    public async Task MarkStaleForVideoAsync(
        Guid videoId,
        Guid? markedByOperationId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await MarkClusterEmbeddingsStaleAsync(connection, transaction, [videoId], markedByOperationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VideoClusterHighSignalMatch>> GetHighSignalMatchesAsync(
        Guid videoId,
        double thresholdPercent,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(thresholdPercent) || thresholdPercent < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdPercent), "The threshold percent must be finite and non-negative.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                rs.id,
                rs.query_text,
                MAX(100 * (1 - (sqe.embedding <=> vce.embedding))) AS similarity_percent,
                vce.provider,
                vce.model,
                vce.dimensions
            FROM public.video_cluster_embeddings AS vce
            INNER JOIN public.search_query_embeddings AS sqe
                ON sqe.provider = vce.provider
               AND sqe.model = vce.model
               AND sqe.dimensions = vce.dimensions
            INNER JOIN public.recent_searches AS rs
                ON rs.id = sqe.recent_search_id
            WHERE vce.video_id = @video_id
              AND vce.is_stale = FALSE
            GROUP BY rs.id, rs.query_text, vce.provider, vce.model, vce.dimensions
            HAVING MAX(100 * (1 - (sqe.embedding <=> vce.embedding))) >= @threshold_percent
            ORDER BY similarity_percent DESC, rs.searched_at DESC, rs.id DESC;
            """,
            connection);
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("threshold_percent", thresholdPercent);

        var matches = new List<VideoClusterHighSignalMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new VideoClusterHighSignalMatch(
                reader.GetGuid(0),
                reader.GetString(1),
                Math.Round(reader.GetDouble(2), 2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5)));
        }

        return matches;
    }

    public async Task<IReadOnlyList<VideoClusterRelatedItem>> GetRelatedVideosAsync(
        Guid videoId,
        int take = 3,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return [];
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                other.video_id,
                MAX(100 * (1 - (base.embedding <=> other.embedding))) AS similarity_percent,
                other.provider,
                other.model,
                other.dimensions
            FROM public.video_cluster_embeddings AS base
            INNER JOIN public.video_cluster_embeddings AS other
                ON other.provider = base.provider
               AND other.model = base.model
               AND other.dimensions = base.dimensions
               AND other.video_id <> base.video_id
            WHERE base.video_id = @video_id
              AND base.is_stale = FALSE
              AND other.is_stale = FALSE
            GROUP BY other.video_id, other.provider, other.model, other.dimensions
            ORDER BY similarity_percent DESC, other.video_id
            LIMIT @limit;
            """,
            connection);
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("limit", take);

        var rawResults = new List<(Guid VideoId, double SimilarityPercent, string Provider, string Model, int Dimensions)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rawResults.Add((
                reader.GetGuid(0),
                Math.Round(reader.GetDouble(1), 2),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        var similarityValues = rawResults.Select(result => result.SimilarityPercent).ToList();
        return rawResults
            .Select(result => new VideoClusterRelatedItem(
                result.VideoId,
                result.SimilarityPercent,
                Math.Round(NormalizeRelativeSimilarity(result.SimilarityPercent, similarityValues), 2),
                result.Provider,
                result.Model,
                result.Dimensions))
            .ToList();
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static async Task<List<ClusterComponent>> LoadComponentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                e.id,
                e.search_document_id,
                e.provider,
                e.model,
                e.dimensions,
                e.content_hash,
                e.embedding,
                sd.document_type,
                sd.source_entity_type,
                sd.source_entity_id,
                sd.search_weight
            FROM public.embeddings AS e
            INNER JOIN public.search_documents AS sd
                ON sd.id = e.search_document_id
            WHERE sd.parent_video_id = @video_id
              AND e.embedding_status = 'succeeded'
            ORDER BY e.provider, e.model, e.dimensions, e.search_document_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("video_id", videoId);

        var components = new List<ClusterComponent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vector = reader.GetFieldValue<Vector>(6);
            components.Add(new ClusterComponent(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                vector.ToArray().Select(static value => (double)value).ToArray(),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10)));
        }

        return components;
    }

    private static Vector? BuildAggregateVector(
        IReadOnlyList<ClusterComponent> components,
        out IReadOnlyList<NormalizedClusterComponent> normalizedComponents)
    {
        var retainedComponents = new List<NormalizedClusterComponent>(components.Count);
        double[]? weightedSum = null;
        double totalWeight = 0d;

        foreach (var component in components)
        {
            var normalizedVector = Normalize(component.Values);
            if (normalizedVector is null)
            {
                continue;
            }

            var weight = component.SearchWeight ?? DefaultWeightForDocumentType(component.DocumentType);
            if (!double.IsFinite(weight) || weight <= 0d)
            {
                continue;
            }

            weightedSum ??= new double[normalizedVector.Length];
            for (var index = 0; index < normalizedVector.Length; index++)
            {
                weightedSum[index] += normalizedVector[index] * weight;
            }

            totalWeight += weight;
            retainedComponents.Add(new NormalizedClusterComponent(
                component.EmbeddingId,
                component.SearchDocumentId,
                component.DocumentType,
                component.SourceEntityType,
                component.SourceEntityId,
                component.ContentHash,
                Math.Round(weight, 6)));
        }

        normalizedComponents = retainedComponents;
        if (weightedSum is null || totalWeight <= 0d)
        {
            return null;
        }

        for (var index = 0; index < weightedSum.Length; index++)
        {
            weightedSum[index] /= totalWeight;
        }

        var normalizedAggregate = Normalize(weightedSum) ?? weightedSum;
        return new Vector(normalizedAggregate.Select(static value => (float)value).ToArray());
    }

    private static async Task<StoredVideoClusterEmbedding> UpsertClusterEmbeddingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid videoId,
        EmbeddingSpace embeddingSpace,
        string contentHash,
        Vector aggregateVector,
        string componentWeightsJson,
        Guid? generatedByOperationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.video_cluster_embeddings (
                id,
                video_id,
                provider,
                model,
                dimensions,
                content_hash,
                embedding,
                component_weights_json,
                is_stale,
                requires_user_approval,
                generated_by_operation_id,
                stale_marked_by_operation_id,
                created_at,
                updated_at
            )
            VALUES (
                @id,
                @video_id,
                @provider,
                @model,
                @dimensions,
                @content_hash,
                @embedding,
                CAST(@component_weights_json AS jsonb),
                FALSE,
                FALSE,
                @generated_by_operation_id,
                NULL,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT ON CONSTRAINT uq_video_cluster_embeddings_identity DO UPDATE SET
                content_hash = EXCLUDED.content_hash,
                embedding = EXCLUDED.embedding,
                component_weights_json = EXCLUDED.component_weights_json,
                is_stale = FALSE,
                requires_user_approval = FALSE,
                generated_by_operation_id = EXCLUDED.generated_by_operation_id,
                stale_marked_by_operation_id = NULL,
                updated_at = CURRENT_TIMESTAMP
            RETURNING id, is_stale, generated_by_operation_id, stale_marked_by_operation_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("provider", embeddingSpace.Provider);
        command.Parameters.AddWithValue("model", embeddingSpace.Model);
        command.Parameters.AddWithValue("dimensions", embeddingSpace.Dimensions);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("embedding", aggregateVector);
        command.Parameters.AddWithValue("component_weights_json", componentWeightsJson);
        command.Parameters.AddWithValue("generated_by_operation_id", (object?)generatedByOperationId ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("PostgreSQL did not return a video-cluster embedding row.");
        }

        return new StoredVideoClusterEmbedding(
            reader.GetGuid(0),
            videoId,
            embeddingSpace.Provider,
            embeddingSpace.Model,
            embeddingSpace.Dimensions,
            contentHash,
            reader.GetBoolean(1),
            componentWeightsJson,
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private static async Task MarkAllVideoRowsStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid videoId,
        Guid? markedByOperationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE public.video_cluster_embeddings
            SET is_stale = TRUE,
                stale_marked_by_operation_id = @marked_by_operation_id,
                updated_at = CURRENT_TIMESTAMP
            WHERE video_id = @video_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("marked_by_operation_id", (object?)markedByOperationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkMissingVideoRowsStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid videoId,
        IReadOnlyCollection<EmbeddingSpace> retainedSpaces,
        Guid? markedByOperationId,
        CancellationToken cancellationToken)
    {
        if (retainedSpaces.Count == 0)
        {
            await MarkAllVideoRowsStaleAsync(connection, transaction, videoId, markedByOperationId, cancellationToken);
            return;
        }

        var predicates = new List<string>(retainedSpaces.Count);
        await using var command = new NpgsqlCommand();
        command.Connection = connection;
        command.Transaction = transaction;
        command.Parameters.AddWithValue("video_id", videoId);
        command.Parameters.AddWithValue("marked_by_operation_id", (object?)markedByOperationId ?? DBNull.Value);

        var index = 0;
        foreach (var space in retainedSpaces)
        {
            predicates.Add($"(provider = @provider_{index} AND model = @model_{index} AND dimensions = @dimensions_{index})");
            command.Parameters.AddWithValue($"provider_{index}", space.Provider);
            command.Parameters.AddWithValue($"model_{index}", space.Model);
            command.Parameters.AddWithValue($"dimensions_{index}", space.Dimensions);
            index++;
        }

        command.CommandText =
            $"""
            UPDATE public.video_cluster_embeddings
            SET is_stale = TRUE,
                stale_marked_by_operation_id = @marked_by_operation_id,
                updated_at = CURRENT_TIMESTAMP
            WHERE video_id = @video_id
              AND NOT ({string.Join(" OR ", predicates)});
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkClusterEmbeddingsStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> videoIds,
        Guid? markedByOperationId,
        CancellationToken cancellationToken)
    {
        if (videoIds.Count == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE public.video_cluster_embeddings
            SET is_stale = TRUE,
                stale_marked_by_operation_id = @marked_by_operation_id,
                updated_at = CURRENT_TIMESTAMP
            WHERE video_id = ANY(@video_ids);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("video_ids", videoIds.ToArray());
        command.Parameters.AddWithValue("marked_by_operation_id", (object?)markedByOperationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static double DefaultWeightForDocumentType(string documentType)
        => documentType switch
        {
            SearchDocumentTypeNames.VideoMetadata => 1.35,
            SearchDocumentTypeNames.Note => 1.25,
            SearchDocumentTypeNames.SegmentTitle => 1.1,
            SearchDocumentTypeNames.TranscriptChunk => 1.0,
            SearchDocumentTypeNames.RepositoryReadmeChunk => 0.95,
            SearchDocumentTypeNames.ExternalResourceMetadata => 0.9,
            SearchDocumentTypeNames.ScrapedPageText => 0.85,
            _ => 1.0
        };

    private static string ComputeClusterContentHash(EmbeddingSpace embeddingSpace, IReadOnlyList<NormalizedClusterComponent> components)
    {
        var builder = new StringBuilder();
        builder.Append(embeddingSpace.Provider)
            .Append('|')
            .Append(embeddingSpace.Model)
            .Append('|')
            .Append(embeddingSpace.Dimensions);

        foreach (var component in components.OrderBy(component => component.SearchDocumentId))
        {
            builder.Append('|')
                .Append(component.SearchDocumentId)
                .Append(':')
                .Append(component.EmbeddingId)
                .Append(':')
                .Append(component.ContentHash)
                .Append(':')
                .Append(component.Weight.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static double[]? Normalize(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var magnitude = Math.Sqrt(values.Sum(value => value * value));
        if (magnitude <= 0d || !double.IsFinite(magnitude))
        {
            return null;
        }

        var normalized = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            normalized[index] = values[index] / magnitude;
        }

        return normalized;
    }

    private static double NormalizeRelativeSimilarity(double score, IReadOnlyList<double> corpusScores)
    {
        if (corpusScores.Count == 0)
        {
            return 0d;
        }

        var min = corpusScores.Min();
        var max = corpusScores.Max();
        if (Math.Abs(max - min) < 0.0000001d)
        {
            return 100d;
        }

        return 100d * (score - min) / (max - min);
    }

    private sealed record EmbeddingSpace(string Provider, string Model, int Dimensions);

    private sealed record ClusterComponent(
        Guid EmbeddingId,
        Guid SearchDocumentId,
        string Provider,
        string Model,
        int Dimensions,
        string ContentHash,
        double[] Values,
        string DocumentType,
        string SourceEntityType,
        Guid SourceEntityId,
        double? SearchWeight);

    private sealed record NormalizedClusterComponent(
        Guid EmbeddingId,
        Guid SearchDocumentId,
        string DocumentType,
        string SourceEntityType,
        Guid SourceEntityId,
        string ContentHash,
        double Weight);
}
