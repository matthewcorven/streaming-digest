using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class FirstUserSetupServiceTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-first-user-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task InitializeAsync_creates_one_hashed_user_without_forced_password_change()
    {
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var result = await service.InitializeAsync(_connectionString!, "founder", "setup-passw0rd!");

        Assert.True(result.Succeeded);
        Assert.Equal(FirstUserInitializationOutcome.Success, result.Outcome);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT username, password_hash, password_hash_algorithm, must_change_password FROM public.app_users", connection);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("founder", reader.GetString(0));
        Assert.Equal("argon2id", reader.GetString(2));
        Assert.False(reader.GetBoolean(3));
        Assert.NotEqual("setup-passw0rd!", reader.GetString(1));
        Assert.True(AppAuthService.VerifyPassword("setup-passw0rd!", reader.GetString(1)));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task InitializeAsync_rejects_duplicate_initialization_once_a_user_exists()
    {
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var firstResult = await service.InitializeAsync(_connectionString!, "founder", "setup-passw0rd!");
        var secondResult = await service.InitializeAsync(_connectionString!, "another", "another-passw0rd!");

        Assert.True(firstResult.Succeeded);
        Assert.Equal(FirstUserInitializationOutcome.AlreadyInitialized, secondResult.Outcome);
        Assert.False(secondResult.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("12345678901")]
    public async Task InitializeAsync_rejects_weak_passwords(string password)
    {
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var result = await service.InitializeAsync(_connectionString!, "founder", password);

        Assert.Equal(FirstUserInitializationOutcome.WeakPassword, result.Outcome);
        Assert.False(result.Succeeded);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM public.app_users", connection);
        var userCount = Convert.ToInt64(await command.ExecuteScalarAsync());
        Assert.Equal(0, userCount);
    }

    [Fact]
    public async Task GetStatusAsync_returns_setup_required_when_no_users_exist()
    {
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var status = await service.GetStatusAsync(_connectionString!);

        Assert.True(status.SetupRequired);
        Assert.Equal(0, status.UserCount);
    }

    [Fact]
    public async Task GetStatusAsync_returns_setup_required_when_only_bootstrap_style_user_exists_and_no_channels_exist()
    {
        await SeedUserAsync("bootstrap", mustChangePassword: true);
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var status = await service.GetStatusAsync(_connectionString!);

        Assert.True(status.SetupRequired);
        Assert.Equal(1, status.UserCount);
    }

    [Fact]
    public async Task GetStatusAsync_returns_not_required_when_password_is_set_even_without_channels()
    {
        await SeedUserAsync("founder", mustChangePassword: false);
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var status = await service.GetStatusAsync(_connectionString!);

        Assert.False(status.SetupRequired);
        Assert.Equal(1, status.UserCount);
    }

    [Fact]
    public async Task GetStatusAsync_returns_not_required_when_a_channel_exists()
    {
        await SeedUserAsync("bootstrap", mustChangePassword: true);
        await SeedChannelAsync();
        var service = new FirstUserSetupService(NullLogger<FirstUserSetupService>.Instance);

        var status = await service.GetStatusAsync(_connectionString!);

        Assert.False(status.SetupRequired);
        Assert.Equal(1, status.UserCount);
    }

    private async Task SeedUserAsync(string username, bool mustChangePassword)
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
                @password_hash,
                'argon2id',
                @must_change_password,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP)
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("password_hash", AppAuthService.HashPassword("setup-passw0rd!"));
        command.Parameters.AddWithValue("must_change_password", mustChangePassword);

        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedChannelAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.channels (
                id,
                youtube_channel_id,
                name_original,
                profile_url,
                source_url,
                created_at,
                updated_at)
            VALUES (
                @id,
                @youtube_channel_id,
                @name_original,
                @profile_url,
                @source_url,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP)
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("youtube_channel_id", "UC-test-channel");
        command.Parameters.AddWithValue("name_original", "Test Channel");
        command.Parameters.AddWithValue("profile_url", "https://www.youtube.com/@testchannel");
        command.Parameters.AddWithValue("source_url", "https://www.youtube.com/@testchannel");

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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL first-user test container.");
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
            // Best effort cleanup.
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
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL first-user test container to become ready.");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}