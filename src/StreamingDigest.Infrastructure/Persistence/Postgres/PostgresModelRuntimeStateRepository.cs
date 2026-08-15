using System.Text.Json;
using Npgsql;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresModelRuntimeStateRepository : IModelRuntimeStateRepository
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PostgresModelRuntimeStateRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.RuntimeRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Status);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.model_runtime_state (
                id,
                provider,
                model_id,
                runtime_role,
                status,
                current_operation_id,
                progress_percent,
                last_verified_at,
                last_seen_in_runtime_at,
                last_error_summary,
                details_json,
                updated_at
            )
            VALUES (
                @id,
                @provider,
                @model_id,
                @runtime_role,
                @status,
                @current_operation_id,
                @progress_percent,
                @last_verified_at,
                @last_seen_in_runtime_at,
                @last_error_summary,
                CAST(@details_json AS jsonb),
                @updated_at
            )
            ON CONFLICT (provider, model_id) DO UPDATE SET
                runtime_role = EXCLUDED.runtime_role,
                status = EXCLUDED.status,
                current_operation_id = EXCLUDED.current_operation_id,
                progress_percent = EXCLUDED.progress_percent,
                last_verified_at = EXCLUDED.last_verified_at,
                last_seen_in_runtime_at = EXCLUDED.last_seen_in_runtime_at,
                last_error_summary = EXCLUDED.last_error_summary,
                details_json = EXCLUDED.details_json,
                updated_at = EXCLUDED.updated_at;
            
            -- Notify API hosts listening on model_state_changed channel with the cross-process event
            SELECT pg_notify(
                'model_state_changed',
                json_build_object(
                    'name', 'model:status',
                    'provider', @provider,
                    'modelId', @model_id,
                    'runtimeRole', @runtime_role,
                    'status', @status,
                    'updatedAt', @updated_at,
                    'dataJson', json_build_object(
                        'provider', @provider,
                        'modelId', @model_id,
                        'status', @status,
                        'currentOperationId', @current_operation_id,
                        'progressPercent', @progress_percent
                    )
                )::text
            );
            """,
            connection);

        command.Parameters.AddWithValue("id", state.Id);
        command.Parameters.AddWithValue("provider", state.Provider.Trim());
        command.Parameters.AddWithValue("model_id", state.ModelId.Trim());
        command.Parameters.AddWithValue("runtime_role", state.RuntimeRole.Trim());
        command.Parameters.AddWithValue("status", state.Status.Trim());
        command.Parameters.AddWithValue("current_operation_id", (object?)state.CurrentOperationId ?? DBNull.Value);
        command.Parameters.AddWithValue("progress_percent", (object?)state.ProgressPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("last_verified_at", (object?)state.LastVerifiedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last_seen_in_runtime_at", (object?)state.LastSeenInRuntimeAt ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error_summary", (object?)state.LastErrorSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("details_json", (object?)state.DetailsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_at", state.UpdatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                id,
                provider,
                model_id,
                runtime_role,
                status,
                current_operation_id,
                progress_percent,
                last_verified_at,
                last_seen_in_runtime_at,
                last_error_summary,
                details_json::text,
                updated_at
            FROM public.model_runtime_state
            WHERE provider = @provider AND model_id = @model_id
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue("provider", provider.Trim());
        command.Parameters.AddWithValue("model_id", modelId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapReaderToState(reader) : null;
    }

    public async Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                id,
                provider,
                model_id,
                runtime_role,
                status,
                current_operation_id,
                progress_percent,
                last_verified_at,
                last_seen_in_runtime_at,
                last_error_summary,
                details_json::text,
                updated_at
            FROM public.model_runtime_state
            WHERE provider = @provider
            ORDER BY model_id;
            """,
            connection);

        command.Parameters.AddWithValue("provider", provider.Trim());

        var states = new List<ModelRuntimeState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(MapReaderToState(reader));
        }

        return states;
    }

    public async Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                id,
                provider,
                model_id,
                runtime_role,
                status,
                current_operation_id,
                progress_percent,
                last_verified_at,
                last_seen_in_runtime_at,
                last_error_summary,
                details_json::text,
                updated_at
            FROM public.model_runtime_state
            ORDER BY provider, model_id;
            """,
            connection);

        var states = new List<ModelRuntimeState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(MapReaderToState(reader));
        }

        return states;
    }

    private static ModelRuntimeState MapReaderToState(NpgsqlDataReader reader)
    {
        return new ModelRuntimeState
        {
            Id = reader.GetGuid(0),
            Provider = reader.GetString(1),
            ModelId = reader.GetString(2),
            RuntimeRole = reader.GetString(3),
            Status = reader.GetString(4),
            CurrentOperationId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
            ProgressPercent = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            LastVerifiedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            LastSeenInRuntimeAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            LastErrorSummary = reader.IsDBNull(9) ? null : reader.GetString(9),
            DetailsJson = reader.IsDBNull(10) ? null : reader.GetString(10),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(11)
        };
    }
}
