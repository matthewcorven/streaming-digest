using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.AppHost;

public sealed class DevelopmentUserCredentialCommandHandler
{
    private readonly ILogger _logger;

    public DevelopmentUserCredentialCommandHandler(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DevelopmentUserCredentialCommandResult> ResetCredentialsAsync(
        string connectionString,
        string? currentUsername,
        string? newUsername,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Development user credential reset requested.");

        var normalizedCurrentUsername = currentUsername?.Trim();
        var normalizedNewUsername = newUsername?.Trim();
        var hasNewUsername = !string.IsNullOrWhiteSpace(normalizedNewUsername);
        var hasNewPassword = !string.IsNullOrWhiteSpace(newPassword);

        if (!hasNewUsername && !hasNewPassword)
        {
            _logger.LogDebug("Development user credential reset rejected because no replacement username or password was provided.");
            return new DevelopmentUserCredentialCommandResult(false, null, "Provide a new username and/or a new password.");
        }

        if (hasNewPassword)
        {
            if (!AppAuthService.IsPasswordStrongEnough(newPassword))
            {
                _logger.LogDebug("Development user credential reset rejected because the replacement password failed the strength requirement.");
                return new DevelopmentUserCredentialCommandResult(false, null, "The password must be at least 12 characters long.");
            }

            _logger.LogDebug("Development user credential reset will update the password.");
        }
        else
        {
            _logger.LogDebug("Development user credential reset will leave the password unchanged.");
        }

        if (hasNewUsername)
        {
            _logger.LogDebug("Development user credential reset will update the username.");
        }
        else
        {
            _logger.LogDebug("Development user credential reset will leave the username unchanged.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var resolvedUser = await ResolveTargetUserAsync(
                connection,
                transaction,
                normalizedCurrentUsername,
                cancellationToken);

            if (!resolvedUser.Success)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DevelopmentUserCredentialCommandResult(false, null, resolvedUser.ErrorMessage);
            }

            if (hasNewUsername)
            {
                await using var updateUsernameCommand = new NpgsqlCommand(
                    """
                    UPDATE public.app_users
                    SET username = @newUsername,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @userId
                    """,
                    connection,
                    transaction);
                updateUsernameCommand.Parameters.AddWithValue("newUsername", normalizedNewUsername!);
                updateUsernameCommand.Parameters.AddWithValue("userId", resolvedUser.UserId!.Value);
                await updateUsernameCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogDebug("Development user credential reset updated the username for user {UserId}.", resolvedUser.UserId.Value);
            }
            else
            {
                _logger.LogDebug("Development user credential reset skipped the username update for user {UserId}.", resolvedUser.UserId!.Value);
            }

            if (hasNewPassword)
            {
                await using var updatePasswordCommand = new NpgsqlCommand(
                    """
                    UPDATE public.app_users
                    SET password_hash = @passwordHash,
                        password_hash_algorithm = 'argon2id',
                        must_change_password = false,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @userId
                    """,
                    connection,
                    transaction);
                updatePasswordCommand.Parameters.AddWithValue("passwordHash", AppAuthService.HashPassword(newPassword!));
                updatePasswordCommand.Parameters.AddWithValue("userId", resolvedUser.UserId!.Value);
                await updatePasswordCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogDebug("Development user credential reset updated the password for user {UserId}.", resolvedUser.UserId.Value);
            }
            else
            {
                _logger.LogDebug("Development user credential reset skipped the password update for user {UserId}.", resolvedUser.UserId!.Value);
            }

            await using var revokeSessionsCommand = new NpgsqlCommand(
                """
                UPDATE public.app_sessions
                SET revoked_at = CURRENT_TIMESTAMP,
                    revoked_reason = 'dev-credential-reset'
                WHERE user_id = @userId
                  AND revoked_at IS NULL
                """,
                connection,
                transaction);
            revokeSessionsCommand.Parameters.AddWithValue("userId", resolvedUser.UserId!.Value);
            var revokedSessions = await revokeSessionsCommand.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogDebug(
                "Development user credential reset revoked {RevokedSessions} active sessions for user {UserId}.",
                revokedSessions,
                resolvedUser.UserId.Value);

            await transaction.CommitAsync(cancellationToken);

            var effectiveUsername = hasNewUsername ? normalizedNewUsername! : resolvedUser.Username!;
            _logger.LogInformation(
                "Development user credential reset completed for user {UserId}. Username={Username}, usernameChanged={UsernameChanged}, passwordChanged={PasswordChanged}.",
                resolvedUser.UserId.Value,
                effectiveUsername,
                hasNewUsername,
                hasNewPassword);

            return new DevelopmentUserCredentialCommandResult(true, effectiveUsername, null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogDebug("Development user credential reset rejected because the requested username already exists.");
            return new DevelopmentUserCredentialCommandResult(false, null, "That username is already taken.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Development user credential reset failed unexpectedly; the transaction was rolled back.");
            throw;
        }
    }

    private async Task<ResolvedUserResult> ResolveTargetUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? currentUsername,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentUsername))
        {
            _logger.LogDebug("Development user credential reset will resolve the target user from the provided current username.");

            await using var command = new NpgsqlCommand(
                """
                SELECT id, username
                FROM public.app_users
                WHERE lower(username) = lower(@currentUsername)
                LIMIT 1
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("currentUsername", currentUsername);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                _logger.LogDebug("Development user credential reset could not find a user named {Username}.", currentUsername);
                return new ResolvedUserResult(false, null, null, $"No user named '{currentUsername}' was found.");
            }

            var userId = reader.GetGuid(0);
            var username = reader.GetString(1);
            _logger.LogDebug("Development user credential reset resolved user {UserId} from the provided current username.", userId);
            return new ResolvedUserResult(true, userId, username, null);
        }

        _logger.LogDebug("Development user credential reset will resolve the target user automatically because no current username was provided.");

        await using var autoResolveCommand = new NpgsqlCommand(
            """
            SELECT id, username
            FROM public.app_users
            ORDER BY created_at
            LIMIT 2
            """,
            connection,
            transaction);

        await using var autoResolveReader = await autoResolveCommand.ExecuteReaderAsync(cancellationToken);
        if (!await autoResolveReader.ReadAsync(cancellationToken))
        {
            _logger.LogDebug("Development user credential reset could not resolve a target user because the database has no app users.");
            return new ResolvedUserResult(false, null, null, "No app user exists yet.");
        }

        var resolvedUserId = autoResolveReader.GetGuid(0);
        var resolvedUsername = autoResolveReader.GetString(1);
        if (await autoResolveReader.ReadAsync(cancellationToken))
        {
            _logger.LogDebug("Development user credential reset could not resolve a target user automatically because multiple app users exist.");
            return new ResolvedUserResult(false, null, null, "Multiple app users exist; specify the current username.");
        }

        _logger.LogDebug("Development user credential reset automatically resolved the only app user {UserId}.", resolvedUserId);
        return new ResolvedUserResult(true, resolvedUserId, resolvedUsername, null);
    }

    private sealed record ResolvedUserResult(bool Success, Guid? UserId, string? Username, string? ErrorMessage);
}

public sealed record DevelopmentUserCredentialCommandResult(bool Success, string? Username, string? ErrorMessage);