using System.Text.Json;
using Dapper;
using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class AppReadinessStateService
{
    private static readonly IReadOnlyList<ReadinessStepDefinition> DefaultStepDefinitions =
    [
        new("admin_password_changed", "Administrator password", true, true),
        new("embedding_model_verified", "Embedding model", true, true),
        new("llm_model_verified", "LLM model", true, true),
        new("audio_to_text_verified", "Audio to text", true, true),
        new("matrix_verified", "Matrix notifications", false, false),
        new("observability_verified", "Observability", false, false),
        new("first_channel_added", "First channel", true, true),
        new("schedule_confirmed", "Schedule", false, true),
        new("backup_path_verified", "Backup path", false, true)
    ];

    public async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required to manage onboarding readiness state.", nameof(connectionString));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS public.app_readiness_checks (
                id uuid PRIMARY KEY,
                check_key text NOT NULL UNIQUE,
                status text NOT NULL,
                last_checked_at timestamptz NULL,
                last_success_at timestamptz NULL,
                last_error_summary text NULL,
                details_json jsonb NULL,
                required_for_core_setup boolean NOT NULL DEFAULT false,
                required_for_full_readiness boolean NOT NULL DEFAULT false,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_app_readiness_checks_status ON public.app_readiness_checks (status);
            """,
            connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OnboardingStatusResponse> GetStatusAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(connectionString, cancellationToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await SeedDefaultDefinitionsAsync(connectionString, cancellationToken);

        var rows = (await connection.QueryAsync<StoredReadinessCheck>(
            """
            SELECT
                check_key AS CheckKey,
                status AS Status,
                last_checked_at AS LastCheckedAt,
                last_success_at AS LastSuccessAt,
                last_error_summary AS LastErrorSummary,
                details_json::text AS DetailsJson,
                required_for_core_setup AS RequiredForCoreSetup,
                required_for_full_readiness AS RequiredForFullReadiness,
                updated_at AS UpdatedAt
            FROM public.app_readiness_checks
            ORDER BY check_key
            """))
            .ToList();

        var rowLookup = rows.ToDictionary(row => row.CheckKey, StringComparer.OrdinalIgnoreCase);
        var steps = new List<OnboardingStepStatus>();
        foreach (var definition in DefaultStepDefinitions)
        {
            if (string.Equals(definition.Key, "admin_password_changed", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(await BuildAdminPasswordStepAsync(connection, definition, rowLookup, cancellationToken));
                continue;
            }

            if (!rowLookup.TryGetValue(definition.Key, out var row))
            {
                steps.Add(CreatePendingStep(definition));
                continue;
            }

            var status = NormalizeStatus(row.Status) ?? "pending";
            var details = ParseDetails(row.DetailsJson);
            steps.Add(new OnboardingStepStatus(
                definition.Key,
                definition.Label,
                status,
                definition.RequiredForCoreSetup,
                definition.RequiredForFullReadiness,
                row.LastCheckedAt,
                row.LastSuccessAt,
                row.LastErrorSummary,
                details));
        }

        var coreSetupCompleted = rowLookup.TryGetValue("core_setup_completed", out var coreSetupRow) && string.Equals(NormalizeStatus(coreSetupRow.Status), "succeeded", StringComparison.OrdinalIgnoreCase);
        var requiredCoreChecksSucceeded = steps.Where(step => step.RequiredForCoreSetup).All(step => string.Equals(step.Status, "succeeded", StringComparison.OrdinalIgnoreCase));
        var isCoreSetupComplete = coreSetupCompleted || requiredCoreChecksSucceeded;
        var isFullyReady = isCoreSetupComplete && steps.Where(step => step.RequiredForFullReadiness).All(step => string.Equals(step.Status, "succeeded", StringComparison.OrdinalIgnoreCase));
        var coreSetupStepStatus = isCoreSetupComplete ? "succeeded" : "blocked";
        var coreSetupStep = new OnboardingStepStatus(
            "core_setup_completed",
            "Core setup completed",
            coreSetupStepStatus,
            false,
            false,
            coreSetupRow?.LastCheckedAt,
            coreSetupRow?.LastSuccessAt,
            coreSetupRow?.LastErrorSummary,
            ParseDetails(coreSetupRow?.DetailsJson));
        steps.Add(coreSetupStep);

        return new OnboardingStatusResponse(isCoreSetupComplete, isFullyReady, steps);
    }

    public async Task<OnboardingStepVerificationResponse> VerifyStepAsync(string connectionString, string stepKey, OnboardingStepVerificationRequest? request, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(connectionString, cancellationToken);

        var normalizedStepKey = string.IsNullOrWhiteSpace(stepKey) ? "unknown_step" : stepKey.Trim();
        var definition = DefaultStepDefinitions.FirstOrDefault(step => string.Equals(step.Key, normalizedStepKey, StringComparison.OrdinalIgnoreCase))
            ?? new ReadinessStepDefinition(normalizedStepKey, normalizedStepKey, false, false);

        var now = DateTimeOffset.UtcNow;
        var preferredStatus = NormalizeStatus(request?.Status);
        var status = string.IsNullOrWhiteSpace(preferredStatus)
            ? "succeeded"
            : preferredStatus;
        var details = request?.Details is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(request.Details, StringComparer.OrdinalIgnoreCase);
        var errorSummary = string.IsNullOrWhiteSpace(request?.ErrorSummary) ? null : request.ErrorSummary.Trim();
        var lastSuccessAt = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) ? (DateTimeOffset?)now : null;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var detailsJson = details.Count == 0 ? null : JsonSerializer.Serialize(details);
        await connection.ExecuteAsync(
            """
            INSERT INTO public.app_readiness_checks (
                id,
                check_key,
                status,
                last_checked_at,
                last_success_at,
                last_error_summary,
                details_json,
                required_for_core_setup,
                required_for_full_readiness,
                updated_at)
            VALUES (
                @id,
                @checkKey,
                @status,
                @lastCheckedAt,
                @lastSuccessAt,
                @lastErrorSummary,
                CAST(@detailsJson AS jsonb),
                @requiredForCoreSetup,
                @requiredForFullReadiness,
                @updatedAt)
            ON CONFLICT (check_key) DO UPDATE SET
                status = EXCLUDED.status,
                last_checked_at = EXCLUDED.last_checked_at,
                last_success_at = EXCLUDED.last_success_at,
                last_error_summary = EXCLUDED.last_error_summary,
                details_json = EXCLUDED.details_json,
                required_for_core_setup = EXCLUDED.required_for_core_setup,
                required_for_full_readiness = EXCLUDED.required_for_full_readiness,
                updated_at = EXCLUDED.updated_at
            """,
            new
            {
                id = Guid.NewGuid(),
                checkKey = normalizedStepKey,
                status,
                lastCheckedAt = now,
                lastSuccessAt,
                lastErrorSummary = errorSummary,
                detailsJson,
                requiredForCoreSetup = definition.RequiredForCoreSetup,
                requiredForFullReadiness = definition.RequiredForFullReadiness,
                updatedAt = now
            });

        var latestStatus = await GetStatusAsync(connectionString, cancellationToken);
        return new OnboardingStepVerificationResponse(
            new OnboardingStepStatus(
                normalizedStepKey,
                definition.Label,
                status,
                definition.RequiredForCoreSetup,
                definition.RequiredForFullReadiness,
                now,
                lastSuccessAt,
                errorSummary,
                details),
            latestStatus.IsCoreSetupComplete,
            latestStatus.IsFullyReady);
    }

    public async Task<OnboardingCoreSetupCompletionResponse> CompleteCoreSetupAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(connectionString, cancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await SeedDefaultDefinitionsAsync(connectionString, cancellationToken);

        var rows = (await connection.QueryAsync<StoredReadinessCheck>(
            """
            SELECT
                check_key AS CheckKey,
                status AS Status,
                last_checked_at AS LastCheckedAt,
                last_success_at AS LastSuccessAt,
                last_error_summary AS LastErrorSummary,
                details_json::text AS DetailsJson,
                required_for_core_setup AS RequiredForCoreSetup,
                required_for_full_readiness AS RequiredForFullReadiness,
                updated_at AS UpdatedAt
            FROM public.app_readiness_checks
            """))
            .ToList();

        var rowLookup = rows.ToDictionary(row => row.CheckKey, StringComparer.OrdinalIgnoreCase);
        var requiredCoreSteps = DefaultStepDefinitions.Where(step => step.RequiredForCoreSetup).ToList();
        var hasAllRequiredCoreChecks = requiredCoreSteps.All(step =>
        {
            if (!rowLookup.TryGetValue(step.Key, out var row))
            {
                return false;
            }

            return string.Equals(NormalizeStatus(row.Status), "succeeded", StringComparison.OrdinalIgnoreCase);
        });

        var now = DateTimeOffset.UtcNow;
        var finalStatus = hasAllRequiredCoreChecks ? "succeeded" : "blocked";
        var lastSuccessAt = hasAllRequiredCoreChecks ? (DateTimeOffset?)now : null;
        var details = new Dictionary<string, object?>
        {
            ["requiredCoreChecks"] = requiredCoreSteps.Select(step => step.Key).ToArray()
        };

        await connection.ExecuteAsync(
            """
            INSERT INTO public.app_readiness_checks (
                id,
                check_key,
                status,
                last_checked_at,
                last_success_at,
                last_error_summary,
                details_json,
                required_for_core_setup,
                required_for_full_readiness,
                updated_at)
            VALUES (
                @id,
                @checkKey,
                @status,
                @lastCheckedAt,
                @lastSuccessAt,
                @lastErrorSummary,
                CAST(@detailsJson AS jsonb),
                @requiredForCoreSetup,
                @requiredForFullReadiness,
                @updatedAt)
            ON CONFLICT (check_key) DO UPDATE SET
                status = EXCLUDED.status,
                last_checked_at = EXCLUDED.last_checked_at,
                last_success_at = EXCLUDED.last_success_at,
                last_error_summary = EXCLUDED.last_error_summary,
                details_json = EXCLUDED.details_json,
                required_for_core_setup = EXCLUDED.required_for_core_setup,
                required_for_full_readiness = EXCLUDED.required_for_full_readiness,
                updated_at = EXCLUDED.updated_at
            """,
            new
            {
                id = Guid.NewGuid(),
                checkKey = "core_setup_completed",
                status = finalStatus,
                lastCheckedAt = now,
                lastSuccessAt = lastSuccessAt,
                lastErrorSummary = hasAllRequiredCoreChecks ? null : "Required core setup checks are still pending or failed.",
                detailsJson = JsonSerializer.Serialize(details),
                requiredForCoreSetup = false,
                requiredForFullReadiness = false,
                updatedAt = now
            });

        var latestStatus = await GetStatusAsync(connectionString, cancellationToken);
        return new OnboardingCoreSetupCompletionResponse(finalStatus, latestStatus.IsCoreSetupComplete, latestStatus.IsFullyReady);
    }

    private async Task SeedDefaultDefinitionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var definition in DefaultStepDefinitions)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO public.app_readiness_checks (
                    id,
                    check_key,
                    status,
                    last_checked_at,
                    last_success_at,
                    last_error_summary,
                    details_json,
                    required_for_core_setup,
                    required_for_full_readiness,
                    updated_at)
                VALUES (
                    @id,
                    @checkKey,
                    @status,
                    @lastCheckedAt,
                    @lastSuccessAt,
                    @lastErrorSummary,
                    CAST(@detailsJson AS jsonb),
                    @requiredForCoreSetup,
                    @requiredForFullReadiness,
                    @updatedAt)
                ON CONFLICT (check_key) DO UPDATE SET
                    required_for_core_setup = EXCLUDED.required_for_core_setup,
                    required_for_full_readiness = EXCLUDED.required_for_full_readiness,
                    updated_at = EXCLUDED.updated_at
                """,
                new
                {
                    id = Guid.NewGuid(),
                    checkKey = definition.Key,
                    status = "pending",
                    lastCheckedAt = (DateTimeOffset?)null,
                    lastSuccessAt = (DateTimeOffset?)null,
                    lastErrorSummary = (string?)null,
                    detailsJson = (string?)null,
                    requiredForCoreSetup = definition.RequiredForCoreSetup,
                    requiredForFullReadiness = definition.RequiredForFullReadiness,
                    updatedAt = DateTimeOffset.UtcNow
                });
        }
    }

    private static async Task<OnboardingStepStatus> BuildAdminPasswordStepAsync(
        NpgsqlConnection connection,
        ReadinessStepDefinition definition,
        IReadOnlyDictionary<string, StoredReadinessCheck> rowLookup,
        CancellationToken cancellationToken)
    {
        var userPasswordState = await TryGetUserPasswordStateAsync(connection, cancellationToken);
        if (userPasswordState is { TotalUsers: > 0, UsersRequiringPasswordChange: 0 })
        {
            return new OnboardingStepStatus(
                definition.Key,
                definition.Label,
                "succeeded",
                definition.RequiredForCoreSetup,
                definition.RequiredForFullReadiness,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                new Dictionary<string, object?>
                {
                    ["derivedFromUserState"] = true,
                    ["userCount"] = userPasswordState.TotalUsers
                });
        }

        if (rowLookup.TryGetValue(definition.Key, out var storedRow))
        {
            var status = NormalizeStatus(storedRow.Status) ?? "pending";
            var details = ParseDetails(storedRow.DetailsJson);
            return new OnboardingStepStatus(
                definition.Key,
                definition.Label,
                status,
                definition.RequiredForCoreSetup,
                definition.RequiredForFullReadiness,
                storedRow.LastCheckedAt,
                storedRow.LastSuccessAt,
                storedRow.LastErrorSummary,
                details);
        }

        return CreatePendingStep(definition);
    }

    private static async Task<UserPasswordState?> TryGetUserPasswordStateAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT
                    COUNT(*) AS total_users,
                    COUNT(*) FILTER (WHERE must_change_password) AS users_requiring_password_change
                FROM public.app_users
                """,
                connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var totalUsers = reader.GetInt64(0);
            var usersRequiringPasswordChange = reader.GetInt64(1);
            await reader.CloseAsync();
            return new UserPasswordState(totalUsers, usersRequiringPasswordChange);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, PostgresErrorCodes.UndefinedTable, StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static OnboardingStepStatus CreatePendingStep(ReadinessStepDefinition definition)
    {
        return new OnboardingStepStatus(
            definition.Key,
            definition.Label,
            "pending",
            definition.RequiredForCoreSetup,
            definition.RequiredForFullReadiness,
            null,
            null,
            null,
            new Dictionary<string, object?>());
    }

    private static Dictionary<string, object?> ParseDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(detailsJson) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "passed" or "complete" or "completed" => "succeeded",
            "failed" or "errored" or "error" => "failed",
            "running" or "in_progress" or "in-progress" => "running",
            _ => status.Trim()
        };
    }

    private sealed record ReadinessStepDefinition(string Key, string Label, bool RequiredForCoreSetup, bool RequiredForFullReadiness);

    private sealed class StoredReadinessCheck
    {
        public string CheckKey { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset? LastCheckedAt { get; init; }
        public DateTimeOffset? LastSuccessAt { get; init; }
        public string? LastErrorSummary { get; init; }
        public string? DetailsJson { get; init; }
        public bool RequiredForCoreSetup { get; init; }
        public bool RequiredForFullReadiness { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed record UserPasswordState(long TotalUsers, long UsersRequiringPasswordChange);
}

public sealed record OnboardingStepVerificationRequest(string? Status = null, string? ErrorSummary = null, Dictionary<string, object?>? Details = null);

public sealed record OnboardingStatusResponse(bool IsCoreSetupComplete, bool IsFullyReady, IReadOnlyList<OnboardingStepStatus> Steps);

public sealed record OnboardingStepStatus(
    string Key,
    string Label,
    string Status,
    bool RequiredForCoreSetup,
    bool RequiredForFullReadiness,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    string? ErrorSummary,
    Dictionary<string, object?> Details);

public sealed record OnboardingStepVerificationResponse(OnboardingStepStatus Step, bool IsCoreSetupComplete, bool IsFullyReady);

public sealed record OnboardingCoreSetupCompletionResponse(string Status, bool IsCoreSetupComplete, bool IsFullyReady);
