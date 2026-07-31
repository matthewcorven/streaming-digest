using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using StreamingDigest.Application;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresSearchDocumentEmbeddingStore : ISearchDocumentEmbeddingStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmbeddingService _embeddingService;

    public PostgresSearchDocumentEmbeddingStore(string connectionString, IEmbeddingService embeddingService)
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

    public async Task DeleteForVideoScopeAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.search_documents WHERE parent_video_id = @parent_video_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("parent_video_id", videoId);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await MarkClusterEmbeddingsStaleAsync(connection, transaction, [videoId], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteForSourceAsync(string sourceEntityType, Guid sourceEntityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affectedVideoIds = await LoadAffectedVideoIdsForSourceAsync(
            connection,
            transaction,
            sourceEntityType,
            sourceEntityId,
            cancellationToken);

        await using var command = new NpgsqlCommand(
            "DELETE FROM public.search_documents WHERE source_entity_type = @source_entity_type AND source_entity_id = @source_entity_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("source_entity_type", sourceEntityType);
        command.Parameters.AddWithValue("source_entity_id", sourceEntityId);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await MarkClusterEmbeddingsStaleAsync(connection, transaction, affectedVideoIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredSearchDocumentEmbedding>> StoreAsync(
        IEnumerable<GeneratedSearchDocument> documents,
        Guid? generatedByOperationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var candidates = documents.ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var embeddingCache = new Dictionary<string, CachedEmbedding>(StringComparer.Ordinal);
        var storedEmbeddings = new List<StoredSearchDocumentEmbedding>(candidates.Count);
        var affectedVideoIds = new HashSet<Guid>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var document in candidates)
        {
            if (!TryBuildEmbeddingInput(document, out var sourceText))
            {
                var deletedParentVideoId = await DeleteSearchDocumentByIdentityAsync(connection, transaction, document, cancellationToken);
                if (deletedParentVideoId is Guid deletedVideoId)
                {
                    affectedVideoIds.Add(deletedVideoId);
                }

                continue;
            }

            var upsertedDocument = await UpsertSearchDocumentAsync(connection, transaction, document, cancellationToken);
            if (upsertedDocument.ShouldMarkClusterStale && upsertedDocument.ParentVideoId is Guid parentVideoId)
            {
                affectedVideoIds.Add(parentVideoId);
            }

            if (!embeddingCache.TryGetValue(document.ContentHash, out var cachedEmbedding))
            {
                var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(sourceText!, cancellationToken);
                cachedEmbedding = new CachedEmbedding(
                    embeddingResult,
                    ComputeSha256(sourceText!),
                    new Vector(embeddingResult.Values.Select(static value => (float)value).ToArray()));
                embeddingCache[document.ContentHash] = cachedEmbedding;
            }

            var embeddingId = await UpsertEmbeddingAsync(
                connection,
                transaction,
                upsertedDocument.Id,
                document.ContentHash,
                cachedEmbedding,
                generatedByOperationId,
                cancellationToken);

            storedEmbeddings.Add(new StoredSearchDocumentEmbedding(
                upsertedDocument.Id,
                embeddingId,
                cachedEmbedding.Result.Provider,
                cachedEmbedding.Result.Model,
                cachedEmbedding.Result.Dimensions,
                document.ContentHash,
                cachedEmbedding.SourceTextHash));
        }

        await MarkClusterEmbeddingsStaleAsync(connection, transaction, affectedVideoIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return storedEmbeddings;
    }

    private static async Task<Guid?> DeleteSearchDocumentByIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GeneratedSearchDocument document,
        CancellationToken cancellationToken)
    {
        var existingDocument = await GetExistingSearchDocumentAsync(connection, transaction, document, cancellationToken);
        if (existingDocument is null)
        {
            return null;
        }

        await using var command = new NpgsqlCommand(
            """
            DELETE FROM public.search_documents
            WHERE parent_video_id IS NOT DISTINCT FROM @parent_video_id
              AND source_entity_type = @source_entity_type
              AND source_entity_id = @source_entity_id
              AND source_field_name IS NOT DISTINCT FROM @source_field_name
              AND chunk_index IS NOT DISTINCT FROM @chunk_index;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("parent_video_id", (object?)document.ParentVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_entity_type", document.SourceEntityType);
        command.Parameters.AddWithValue("source_entity_id", document.SourceEntityId);
        command.Parameters.AddWithValue("source_field_name", (object?)document.SourceFieldName ?? DBNull.Value);
        command.Parameters.AddWithValue("chunk_index", (object?)document.ChunkIndex ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return existingDocument.ParentVideoId;
    }

    private static async Task<UpsertedSearchDocument> UpsertSearchDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GeneratedSearchDocument document,
        CancellationToken cancellationToken)
    {
        var existingDocument = await GetExistingSearchDocumentAsync(connection, transaction, document, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.search_documents (
                id,
                document_type,
                source_entity_type,
                source_entity_id,
                source_field_name,
                chunk_index,
                chunk_start_offset,
                chunk_end_offset,
                token_count,
                search_weight,
                embedding_required,
                parent_video_id,
                parent_segment_id,
                title_effective,
                body_effective,
                metadata_json,
                fts_weight_config,
                content_hash,
                is_stale,
                created_at,
                updated_at
            )
            VALUES (
                @id,
                @document_type,
                @source_entity_type,
                @source_entity_id,
                @source_field_name,
                @chunk_index,
                NULL,
                NULL,
                NULL,
                NULL,
                TRUE,
                @parent_video_id,
                NULL,
                @title_effective,
                @body_effective,
                NULL,
                NULL,
                @content_hash,
                FALSE,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT ON CONSTRAINT uq_search_documents_identity DO UPDATE SET
                document_type = EXCLUDED.document_type,
                source_entity_type = EXCLUDED.source_entity_type,
                source_field_name = EXCLUDED.source_field_name,
                chunk_index = EXCLUDED.chunk_index,
                parent_video_id = EXCLUDED.parent_video_id,
                title_effective = EXCLUDED.title_effective,
                body_effective = EXCLUDED.body_effective,
                content_hash = EXCLUDED.content_hash,
                is_stale = FALSE,
                updated_at = CURRENT_TIMESTAMP
            RETURNING id;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("document_type", document.DocumentType);
        command.Parameters.AddWithValue("source_entity_type", document.SourceEntityType);
        command.Parameters.AddWithValue("source_entity_id", document.SourceEntityId);
        command.Parameters.AddWithValue("source_field_name", (object?)document.SourceFieldName ?? DBNull.Value);
        command.Parameters.AddWithValue("chunk_index", (object?)document.ChunkIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("parent_video_id", (object?)document.ParentVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue("title_effective", (object?)document.TitleEffective ?? DBNull.Value);
        command.Parameters.AddWithValue("body_effective", (object?)document.BodyEffective ?? DBNull.Value);
        command.Parameters.AddWithValue("content_hash", document.ContentHash);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not Guid id)
        {
            throw new InvalidOperationException("PostgreSQL did not return a search document id.");
        }

        var shouldMarkClusterStale =
            document.ParentVideoId is not null
            && (existingDocument is null
                || existingDocument.ContentHash != document.ContentHash
                || existingDocument.ParentVideoId != document.ParentVideoId);

        return new UpsertedSearchDocument(id, document.ParentVideoId, shouldMarkClusterStale);
    }

    private static async Task<Guid> UpsertEmbeddingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid searchDocumentId,
        string contentHash,
        CachedEmbedding embedding,
        Guid? generatedByOperationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.embeddings (
                id,
                search_document_id,
                provider,
                model,
                dimensions,
                content_hash,
                source_text_hash,
                embedding,
                embedding_status,
                error_summary,
                generated_by_operation_id,
                created_at
            )
            VALUES (
                @id,
                @search_document_id,
                @provider,
                @model,
                @dimensions,
                @content_hash,
                @source_text_hash,
                @embedding,
                'succeeded',
                NULL,
                @generated_by_operation_id,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT ON CONSTRAINT uq_embeddings_identity DO UPDATE SET
                content_hash = EXCLUDED.content_hash,
                source_text_hash = EXCLUDED.source_text_hash,
                embedding = EXCLUDED.embedding,
                embedding_status = 'succeeded',
                error_summary = NULL,
                generated_by_operation_id = EXCLUDED.generated_by_operation_id
            RETURNING id;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("search_document_id", searchDocumentId);
        command.Parameters.AddWithValue("provider", embedding.Result.Provider);
        command.Parameters.AddWithValue("model", embedding.Result.Model);
        command.Parameters.AddWithValue("dimensions", embedding.Result.Dimensions);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("source_text_hash", embedding.SourceTextHash);
        command.Parameters.AddWithValue("embedding", embedding.Vector);
        command.Parameters.AddWithValue("generated_by_operation_id", (object?)generatedByOperationId ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id
            ? id
            : throw new InvalidOperationException("PostgreSQL did not return an embedding id.");
    }

    private static bool TryBuildEmbeddingInput(GeneratedSearchDocument document, out string? sourceText)
    {
        var parts = new[] { document.TitleEffective, document.BodyEffective }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();

        if (parts.Length == 0)
        {
            sourceText = null;
            return false;
        }

        sourceText = string.Join("\n\n", parts);
        return true;
    }

    private static string BuildEmbeddingInput(GeneratedSearchDocument document)
    {
        if (!TryBuildEmbeddingInput(document, out var sourceText))
        {
            throw new InvalidOperationException($"Search document '{document.SourceEntityId}' has no content to embed.");
        }

        return sourceText!;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static async Task<ExistingSearchDocument?> GetExistingSearchDocumentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GeneratedSearchDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, parent_video_id, content_hash
            FROM public.search_documents
            WHERE parent_video_id IS NOT DISTINCT FROM @parent_video_id
              AND source_entity_type = @source_entity_type
              AND source_entity_id = @source_entity_id
              AND source_field_name IS NOT DISTINCT FROM @source_field_name
              AND chunk_index IS NOT DISTINCT FROM @chunk_index
            LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("parent_video_id", (object?)document.ParentVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_entity_type", document.SourceEntityType);
        command.Parameters.AddWithValue("source_entity_id", document.SourceEntityId);
        command.Parameters.AddWithValue("source_field_name", (object?)document.SourceFieldName ?? DBNull.Value);
        command.Parameters.AddWithValue("chunk_index", (object?)document.ChunkIndex ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingSearchDocument(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2));
    }

    private static async Task<HashSet<Guid>> LoadAffectedVideoIdsForSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sourceEntityType,
        Guid sourceEntityId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT DISTINCT parent_video_id
            FROM public.search_documents
            WHERE source_entity_type = @source_entity_type
              AND source_entity_id = @source_entity_id
              AND parent_video_id IS NOT NULL;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("source_entity_type", sourceEntityType);
        command.Parameters.AddWithValue("source_entity_id", sourceEntityId);

        var videoIds = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            videoIds.Add(reader.GetGuid(0));
        }

        return videoIds;
    }

    private static async Task MarkClusterEmbeddingsStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> videoIds,
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
                updated_at = CURRENT_TIMESTAMP
            WHERE video_id = ANY(@video_ids);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("video_ids", videoIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private sealed record CachedEmbedding(EmbeddingGenerationResult Result, string SourceTextHash, Vector Vector);
    private sealed record ExistingSearchDocument(Guid Id, Guid? ParentVideoId, string ContentHash);
    private sealed record UpsertedSearchDocument(Guid Id, Guid? ParentVideoId, bool ShouldMarkClusterStale);
}
