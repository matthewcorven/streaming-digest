using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace StreamingDigest.IntegrationTests;

public sealed class StartupMigrationIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "postgres:16-alpine";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";

    private readonly string _containerName = $"streaming-digest-startup-migration-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        _factory = new StartupMigrationWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task ApiStartup_AppliesPostgresMigrationAndRecordsDbSchemaVersion()
    {
        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT
                to_regclass('public.app_users') IS NOT NULL AS has_app_users,
                EXISTS (SELECT 1 FROM public.app_settings WHERE key = 'system.dbSchemaVersion') AS has_db_schema_state;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync(
            "docker",
            [
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
            ]);

        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL startup migration test container.");
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
            await RunProcessAsync("docker", ["rm", "-f", _containerName]);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 90; attempt++)
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL startup migration test container to become ready after 90 seconds.");
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

    private sealed class StartupMigrationWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:streamingdigest", connectionString);
            builder.UseSetting("ConnectionStrings:postgres", connectionString);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:streamingdigest"] = connectionString,
                    ["ConnectionStrings:postgres"] = connectionString,
                    ["BOOTSTRAP_ADMIN_USERNAME"] = BootstrapUsername,
                    ["BOOTSTRAP_ADMIN_PASSWORD"] = BootstrapPassword
                });
            });
        }
    }
}
