using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

[Collection("AuthFlow")]
public sealed class FirstUserSetupIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-first-user-integration-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new FirstUserSetupWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Setup_status_and_initialize_transition_from_zero_users_to_one_user()
    {
        using var client = CreateClient();

        var initialStatus = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        Assert.NotNull(initialStatus);
        Assert.True(initialStatus!.SetupRequired);
        Assert.Equal(0, initialStatus.UserCount);

        var initializeResponse = await client.PostAsJsonAsync("/api/setup/initialize", new
        {
            username = "founder",
            password = "setup-passw0rd!"
        });

        Assert.Equal(HttpStatusCode.Created, initializeResponse.StatusCode);

        var finalStatus = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        Assert.NotNull(finalStatus);
        Assert.False(finalStatus!.SetupRequired);
        Assert.Equal(1, finalStatus.UserCount);

        var duplicateResponse = await client.PostAsJsonAsync("/api/setup/initialize", new
        {
            username = "another",
            password = "another-passw0rd!"
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Created_credentials_can_sign_in_after_setup_redirect_flow()
    {
        using var client = CreateClient();

        var initializeResponse = await client.PostAsJsonAsync("/api/setup/initialize", new
        {
            username = "founder",
            password = "setup-passw0rd!"
        });
        Assert.Equal(HttpStatusCode.Created, initializeResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "founder",
            password = "setup-passw0rd!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.NotNull(me);
        Assert.Equal("founder", me!.Username);
        Assert.False(me.MustChangePassword);
    }

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL first-user integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL first-user integration test container to become ready.");
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

    private sealed class FirstUserSetupWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
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
                    ["ConnectionStrings:postgres"] = connectionString
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.AddSingleton<IEmbeddingService, FakeEmbeddingService>();
            });
        }
    }

    private sealed record SetupStatusResponse(bool SetupRequired, long UserCount);

    private sealed record AuthMeResponse(string Username, bool MustChangePassword);
}