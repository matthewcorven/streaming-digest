using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class BootstrapAdminUserService
{
    private const string UsernameEnvironmentVariable = "BOOTSTRAP_ADMIN_USERNAME";
    private const string PasswordEnvironmentVariable = "BOOTSTRAP_ADMIN_PASSWORD";

    private readonly IConfiguration _configuration;
    private readonly ILogger<BootstrapAdminUserService> _logger;
    public BootstrapAdminUserService(IConfiguration configuration, ILogger<BootstrapAdminUserService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureBootstrapAdminUserAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var username = ResolveBootstrapSetting(UsernameEnvironmentVariable, "ADMIN_USERNAME", "BOOTSTRAP_USERNAME", "STREAMING_DIGEST_ADMIN_USERNAME", "STREAMING_DIGEST_BOOTSTRAP_ADMIN_USERNAME");
        var password = ResolveBootstrapSetting(PasswordEnvironmentVariable, "ADMIN_PASSWORD", "BOOTSTRAP_PASSWORD", "STREAMING_DIGEST_ADMIN_PASSWORD", "STREAMING_DIGEST_BOOTSTRAP_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogDebug("Bootstrap admin credentials were not provided; skipping bootstrap admin user creation.");
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var countCommand = new NpgsqlCommand("SELECT COUNT(*) FROM public.app_users", connection);
        var userCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (userCount > 0)
        {
            _logger.LogDebug("App users already exist; skipping bootstrap admin user creation.");
            return;
        }

        var passwordHash = AppAuthService.HashPassword(password);
        var userId = Guid.NewGuid();

        await using var insertCommand = new NpgsqlCommand(
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
                true,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP)
            """,
            connection);

        insertCommand.Parameters.AddWithValue("id", userId);
        insertCommand.Parameters.AddWithValue("username", username);
        insertCommand.Parameters.AddWithValue("passwordHash", passwordHash);
        insertCommand.Parameters.AddWithValue("passwordHashAlgorithm", "argon2id");

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Bootstrap admin user '{Username}' created.", username);
    }

    private string? ResolveBootstrapSetting(params string[] settingNames)
    {
        foreach (var settingName in settingNames)
        {
            var value = _configuration[settingName];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var environmentValue = Environment.GetEnvironmentVariable(settingName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }
        }

        return null;
    }
}
