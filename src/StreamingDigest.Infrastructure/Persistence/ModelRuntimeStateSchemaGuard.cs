using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class ModelRuntimeStateSchemaGuard : IModelRuntimeStateSchemaGuard
{
    public async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required to manage model runtime state.", nameof(connectionString));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS public.model_runtime_state (
                id uuid PRIMARY KEY,
                provider text NOT NULL,
                model_id text NOT NULL,
                runtime_role text NOT NULL,
                status text NOT NULL,
                current_operation_id uuid NULL REFERENCES public.operations(id) ON DELETE SET NULL,
                progress_percent integer NULL,
                last_verified_at timestamptz NULL,
                last_seen_in_runtime_at timestamptz NULL,
                last_error_summary text NULL,
                details_json jsonb NULL,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT uq_model_runtime_state_provider_model_id UNIQUE (provider, model_id)
            );
            CREATE INDEX IF NOT EXISTS idx_model_runtime_state_provider
                ON public.model_runtime_state (provider);
            CREATE INDEX IF NOT EXISTS idx_model_runtime_state_status
                ON public.model_runtime_state (status);
            CREATE INDEX IF NOT EXISTS idx_model_runtime_state_provider_status
                ON public.model_runtime_state (provider, status);
            CREATE INDEX IF NOT EXISTS idx_model_runtime_state_updated_at
                ON public.model_runtime_state (updated_at);
            """,
            connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
