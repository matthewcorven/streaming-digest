using Microsoft.Extensions.Logging;
using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class FirstUserSetupService
{
    private readonly ILogger<FirstUserSetupService> _logger;

    public FirstUserSetupService(ILogger<FirstUserSetupService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FirstUserSetupStatus> GetStatusAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        long userCount;
        long usersRequiringPasswordChange;
        await using (var userStateCommand = new NpgsqlCommand(
                         """
                         SELECT
                             COUNT(*) AS total_users,
                             COUNT(*) FILTER (WHERE must_change_password) AS users_requiring_password_change
                         FROM public.app_users
                         """,
                         connection))
        {
            await using var reader = await userStateCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            userCount = reader.GetInt64(0);
            usersRequiringPasswordChange = reader.GetInt64(1);
            await reader.CloseAsync();
        }

        long channelCount;
        await using (var channelCountCommand = new NpgsqlCommand("SELECT COUNT(*) FROM public.channels", connection))
        {
            channelCount = Convert.ToInt64(await channelCountCommand.ExecuteScalarAsync(cancellationToken));
        }

        var setupRequired = EvaluateSetupRequired(userCount, usersRequiringPasswordChange, channelCount);
        _logger.LogDebug(
            "Computed first-user setup status. SetupRequired={SetupRequired}, UserCount={UserCount}, UsersRequiringPasswordChange={UsersRequiringPasswordChange}, ChannelCount={ChannelCount}.",
            setupRequired,
            userCount,
            usersRequiringPasswordChange,
            channelCount);

        return new FirstUserSetupStatus(setupRequired, userCount);
    }

    public static bool EvaluateSetupRequired(long userCount, long usersRequiringPasswordChange, long channelCount)
    {
        if (userCount == 0)
        {
            return true;
        }

        if (usersRequiringPasswordChange == userCount && channelCount == 0)
        {
            return true;
        }

        return false;
    }

    public async Task<FirstUserInitializationResult> InitializeAsync(string connectionString, string username, string password, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return new FirstUserInitializationResult(FirstUserInitializationOutcome.InvalidUsername, null, "A username is required.");
        }

        if (!AppAuthService.IsPasswordStrongEnough(password))
        {
            return new FirstUserInitializationResult(FirstUserInitializationOutcome.WeakPassword, null, "The password must be at least 12 characters long.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("LOCK TABLE public.app_users IN EXCLUSIVE MODE", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        long userCount;
        await using (var countCommand = new NpgsqlCommand("SELECT COUNT(*) FROM public.app_users", connection, transaction))
        {
            userCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        if (userCount > 0)
        {
            _logger.LogDebug("First-user initialization was rejected because {UserCount} users already exist.", userCount);
            await transaction.RollbackAsync(cancellationToken);
            return new FirstUserInitializationResult(FirstUserInitializationOutcome.AlreadyInitialized, null, "Setup has already been completed.");
        }

        var userId = Guid.NewGuid();
        var passwordHash = AppAuthService.HashPassword(password);

        await using (var insertCommand = new NpgsqlCommand(
                         """
                         INSERT INTO public.app_users (
                             id,
                             username,
                             password_hash,
                             password_hash_algorithm,
                             must_change_password,
                             created_at,
                             updated_at)
                         VALUES (
                             @id,
                             @username,
                             @passwordHash,
                             @passwordHashAlgorithm,
                             false,
                             CURRENT_TIMESTAMP,
                             CURRENT_TIMESTAMP)
                         """,
                         connection,
                         transaction))
        {
            insertCommand.Parameters.AddWithValue("id", userId);
            insertCommand.Parameters.AddWithValue("username", normalizedUsername);
            insertCommand.Parameters.AddWithValue("passwordHash", passwordHash);
            insertCommand.Parameters.AddWithValue("passwordHashAlgorithm", "argon2id");

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("First-time setup created user '{Username}'.", normalizedUsername);
        return new FirstUserInitializationResult(FirstUserInitializationOutcome.Success, normalizedUsername, null);
    }
}

public sealed record FirstUserSetupStatus(bool SetupRequired, long UserCount);

public sealed record FirstUserInitializationResult(FirstUserInitializationOutcome Outcome, string? Username, string? ErrorMessage)
{
    public bool Succeeded => Outcome == FirstUserInitializationOutcome.Success;
}

public enum FirstUserInitializationOutcome
{
    Success,
    AlreadyInitialized,
    InvalidUsername,
    WeakPassword
}