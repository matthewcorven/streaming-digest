using Npgsql;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence;

/// <summary>
/// Raw-Npgsql store for the <c>operations</c> table, used by the model download pipeline in
/// both the API process (persist-before-enqueue) and the worker process (transitions). All
/// writes propagate failures to the caller — the handoff contract requires real durability.
/// </summary>
public sealed class PostgresOperationStore : IOperationStore
{
    private readonly string _connectionString;

    public PostgresOperationStore(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task PersistAsync(OperationRecord operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.operations (
                id,
                operation_type,
                status,
                risk_level,
                requested_by,
                related_entity_type,
                related_entity_id,
                hangfire_job_id,
                started_at,
                completed_at,
                summary_json,
                error_summary,
                created_at,
                updated_at
            )
            VALUES (
                @id,
                @operation_type,
                @status,
                @risk_level,
                @requested_by,
                @related_entity_type,
                @related_entity_id,
                @hangfire_job_id,
                @started_at,
                @completed_at,
                CAST(@summary_json AS jsonb),
                @error_summary,
                @created_at,
                @updated_at
            )
            ON CONFLICT (id) DO UPDATE SET
                operation_type = EXCLUDED.operation_type,
                status = EXCLUDED.status,
                risk_level = EXCLUDED.risk_level,
                requested_by = EXCLUDED.requested_by,
                related_entity_type = EXCLUDED.related_entity_type,
                related_entity_id = EXCLUDED.related_entity_id,
                hangfire_job_id = COALESCE(EXCLUDED.hangfire_job_id, public.operations.hangfire_job_id),
                started_at = EXCLUDED.started_at,
                completed_at = EXCLUDED.completed_at,
                summary_json = EXCLUDED.summary_json,
                error_summary = EXCLUDED.error_summary,
                updated_at = EXCLUDED.updated_at;
            """,
            connection);

        command.Parameters.AddWithValue("id", operation.Id);
        command.Parameters.AddWithValue("operation_type", operation.OperationType);
        command.Parameters.AddWithValue("status", operation.Status);
        command.Parameters.AddWithValue("risk_level", (object?)operation.RiskLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("requested_by", (object?)operation.RequestedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("related_entity_type", (object?)operation.RelatedEntityType ?? DBNull.Value);
        command.Parameters.AddWithValue("related_entity_id", (object?)operation.RelatedEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("hangfire_job_id", (object?)operation.HangfireJobId ?? DBNull.Value);
        command.Parameters.AddWithValue("started_at", (object?)operation.StartedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("completed_at", (object?)operation.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("summary_json", (object?)operation.SummaryJson ?? DBNull.Value);
        command.Parameters.AddWithValue("error_summary", (object?)operation.ErrorSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", operation.CreatedAt);
        command.Parameters.AddWithValue("updated_at", operation.UpdatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OperationRecord?> GetByIdAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                id,
                operation_type,
                status,
                risk_level,
                requested_by,
                related_entity_type,
                related_entity_id,
                hangfire_job_id,
                started_at,
                completed_at,
                summary_json::text,
                error_summary,
                created_at,
                updated_at
            FROM public.operations
            WHERE id = @id
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue("id", operationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperationRecord
        {
            Id = reader.GetGuid(0),
            OperationType = reader.GetString(1),
            Status = reader.GetString(2),
            RiskLevel = reader.IsDBNull(3) ? null : reader.GetString(3),
            RequestedBy = reader.IsDBNull(4) ? null : reader.GetString(4),
            RelatedEntityType = reader.IsDBNull(5) ? null : reader.GetString(5),
            RelatedEntityId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
            HangfireJobId = reader.IsDBNull(7) ? null : reader.GetString(7),
            StartedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            CompletedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            SummaryJson = reader.IsDBNull(10) ? null : reader.GetString(10),
            ErrorSummary = reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(12),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(13)
        };
    }

    public async Task UpdateHangfireJobIdAsync(Guid operationId, string hangfireJobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hangfireJobId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            UPDATE public.operations
            SET hangfire_job_id = @hangfire_job_id,
                updated_at = @updated_at
            WHERE id = @id;
            """,
            connection);

        command.Parameters.AddWithValue("id", operationId);
        command.Parameters.AddWithValue("hangfire_job_id", hangfireJobId);
        command.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
