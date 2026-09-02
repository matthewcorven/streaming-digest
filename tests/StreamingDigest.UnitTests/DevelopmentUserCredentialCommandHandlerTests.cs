using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StreamingDigest.AppHost;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class DevelopmentUserCredentialCommandHandlerValidationTests
{
    [Fact]
    public async Task ResetCredentialsAsync_rejects_missing_updates_and_logs_the_validation_branch()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);

        var result = await handler.ResetCredentialsAsync(
            connectionString: "Host=unused;Port=5432;Database=unused;Username=unused;Password=unused",
            currentUsername: null,
            newUsername: null,
            newPassword: null);

        Assert.False(result.Success);
        Assert.Equal("Provide a new username and/or a new password.", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("no replacement username or password was provided", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResetCredentialsAsync_rejects_weak_password_and_logs_the_strength_branch()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);

        var result = await handler.ResetCredentialsAsync(
            connectionString: "Host=unused;Port=5432;Database=unused;Username=unused;Password=unused",
            currentUsername: null,
            newUsername: null,
            newPassword: "too-short");

        Assert.False(result.Success);
        Assert.Equal("The password must be at least 12 characters long.", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("replacement password failed the strength requirement", StringComparison.Ordinal));
    }
}

public sealed class DevelopmentUserCredentialCommandHandlerDatabaseTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-dev-user-credential-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var migrationRunner = new PostgresMigrationRunner(_connectionString);
        await migrationRunner.ApplyAsync();

        var configuration = new ConfigurationBuilder().Build();
        var authService = new AppAuthService(configuration, NullLogger<AppAuthService>.Instance);
        await authService.EnsureSchemaAsync(_connectionString);
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task ResetCredentialsAsync_updates_the_only_user_without_current_username_and_revokes_sessions()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);
        var userId = Guid.NewGuid();

        await SeedUserAsync(userId, "first-user", "initial-password-123", mustChangePassword: true);
        await SeedSessionAsync(userId, "session-token-1");

        var result = await handler.ResetCredentialsAsync(
            _connectionString!,
            currentUsername: null,
            newUsername: "updated-user",
            newPassword: null);

        Assert.True(result.Success);
        Assert.Equal("updated-user", result.Username);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand(
                         "SELECT username, must_change_password FROM public.app_users WHERE id = @userId",
                         connection))
        {
            command.Parameters.AddWithValue("userId", userId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("updated-user", reader.GetString(0));
            Assert.True(reader.GetBoolean(1));
        }

        await using (var sessionCommand = new NpgsqlCommand(
                         "SELECT revoked_reason FROM public.app_sessions WHERE user_id = @userId",
                         connection))
        {
            sessionCommand.Parameters.AddWithValue("userId", userId);
            var revokedReason = (string?)await sessionCommand.ExecuteScalarAsync();
            Assert.Equal("dev-credential-reset", revokedReason);
        }

        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("resolve the target user automatically", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("leave the password unchanged", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("updated the username", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("skipped the password update", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResetCredentialsAsync_updates_password_for_explicit_username_and_logs_the_explicit_path()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);
        var userId = Guid.NewGuid();

        await SeedUserAsync(userId, "named-user", "original-password-123", mustChangePassword: true);

        var result = await handler.ResetCredentialsAsync(
            _connectionString!,
            currentUsername: "named-user",
            newUsername: null,
            newPassword: "replacement-password-456");

        Assert.True(result.Success);
        Assert.Equal("named-user", result.Username);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT username, password_hash, must_change_password FROM public.app_users WHERE id = @userId",
            connection);
        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("named-user", reader.GetString(0));
        Assert.True(AppAuthService.VerifyPassword("replacement-password-456", reader.GetString(1)));
        Assert.False(reader.GetBoolean(2));

        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("resolve the target user from the provided current username", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("leave the username unchanged", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("updated the password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResetCredentialsAsync_rejects_automatic_resolution_when_multiple_users_exist()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);

        await SeedUserAsync(Guid.NewGuid(), "first-user", "initial-password-123", mustChangePassword: false);
        await SeedUserAsync(Guid.NewGuid(), "second-user", "initial-password-456", mustChangePassword: false);

        var result = await handler.ResetCredentialsAsync(
            _connectionString!,
            currentUsername: null,
            newUsername: "updated-user",
            newPassword: null);

        Assert.False(result.Success);
        Assert.Equal("Multiple app users exist; specify the current username.", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("multiple app users exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResetCredentialsAsync_rejects_duplicate_username_and_logs_the_unique_violation_branch()
    {
        var logger = new TestLogger<DevelopmentUserCredentialCommandHandler>();
        var handler = new DevelopmentUserCredentialCommandHandler(logger);

        await SeedUserAsync(Guid.NewGuid(), "first-user", "initial-password-123", mustChangePassword: false);
        await SeedUserAsync(Guid.NewGuid(), "taken-user", "initial-password-456", mustChangePassword: false);

        var result = await handler.ResetCredentialsAsync(
            _connectionString!,
            currentUsername: "first-user",
            newUsername: "taken-user",
            newPassword: null);

        Assert.False(result.Success);
        Assert.Equal("That username is already taken.", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.LogLevel == LogLevel.Debug && entry.Message.Contains("requested username already exists", StringComparison.Ordinal));
    }

    private async Task SeedUserAsync(Guid userId, string username, string password, bool mustChangePassword)
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
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
                'argon2id',
                @mustChangePassword,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP)
            """,
            connection);

        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("passwordHash", AppAuthService.HashPassword(password));
        command.Parameters.AddWithValue("mustChangePassword", mustChangePassword);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedSessionAsync(Guid userId, string sessionToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.app_sessions (
                id,
                user_id,
                session_token,
                csrf_token,
                expires_at,
                created_at,
                updated_at,
                last_seen_at)
            VALUES (
                @id,
                @userId,
                @sessionToken,
                @csrfToken,
                CURRENT_TIMESTAMP + INTERVAL '1 day',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP)
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("sessionToken", sessionToken);
        command.Parameters.AddWithValue("csrfToken", "csrf-token");
        await command.ExecuteNonQueryAsync();
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-e",
            $"POSTGRES_USER={Username}",
            "-e",
            $"POSTGRES_PASSWORD={Password}",
            "-e",
            $"POSTGRES_DB={DatabaseName}",
            "-p",
            $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the development user credential test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("Timed out waiting for PostgreSQL to accept connections.");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }
}

file sealed class TestLogger<T> : ILogger<T>, ILogger
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

file sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

file sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
}