using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class AppAuthServiceTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-auth-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Authenticate_change_password_and_logout_follow_the_expected_session_flow()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BOOTSTRAP_ADMIN_USERNAME"] = "bootstrap-admin",
                ["BOOTSTRAP_ADMIN_PASSWORD"] = "s3cr3t-passw0rd"
            })
            .Build();

        var bootstrapService = new BootstrapAdminUserService(configuration, NullLogger<BootstrapAdminUserService>.Instance);
        await bootstrapService.EnsureBootstrapAdminUserAsync(_connectionString!);

        var authService = new AppAuthService(configuration, NullLogger<AppAuthService>.Instance);

        var loginResult = await authService.AuthenticateAsync(_connectionString!, "bootstrap-admin", "s3cr3t-passw0rd", "127.0.0.1");
        Assert.True(loginResult.Success);
        Assert.True(loginResult.User!.MustChangePassword);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.SessionToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.CsrfToken));

        var request = new DefaultHttpContext().Request;
        request.Headers.Cookie = $"auth-session={loginResult.SessionToken}";
        var requestAuthenticationResult = await authService.TryAuthenticateRequestAsync(_connectionString!, request);
        Assert.True(requestAuthenticationResult.IsAuthenticated);
        Assert.Equal(loginResult.User.Username, requestAuthenticationResult.User!.Username);
        Assert.Equal(loginResult.CsrfToken, requestAuthenticationResult.CsrfToken);

        var passwordChanged = await authService.ChangePasswordAsync(_connectionString!, loginResult.User.Id, "s3cr3t-passw0rd", "new-password-12345");
        Assert.True(passwordChanged);

        var oldPasswordLogin = await authService.AuthenticateAsync(_connectionString!, "bootstrap-admin", "s3cr3t-passw0rd", "127.0.0.1");
        Assert.False(oldPasswordLogin.Success);
        Assert.Equal(StatusCodes.Status401Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await authService.AuthenticateAsync(_connectionString!, "bootstrap-admin", "new-password-12345", "127.0.0.1");
        Assert.True(newPasswordLogin.Success);
        Assert.False(newPasswordLogin.User!.MustChangePassword);

        var logoutSucceeded = await authService.LogoutAsync(_connectionString!, loginResult.SessionToken);
        Assert.True(logoutSucceeded);

        var afterLogout = await authService.TryAuthenticateRequestAsync(_connectionString!, request);
        Assert.False(afterLogout.IsAuthenticated);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL auth test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_containerName))
        {
            return;
        }

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
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL auth test container to become ready.");
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
