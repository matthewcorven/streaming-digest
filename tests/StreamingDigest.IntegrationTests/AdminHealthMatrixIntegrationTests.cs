using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Web.Models;

namespace StreamingDigest.IntegrationTests;

public sealed class AdminHealthMatrixIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";

    private readonly string _containerName = $"streaming-digest-admin-health-matrix-{Guid.NewGuid():N}";
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

        _factory = new AdminHealthMatrixWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task AdminHealth_reports_recent_matrix_failures_and_successes_in_the_matrix_section()
    {
        await SeedNotificationAsync("sent", null, sentAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await SeedNotificationAsync("pending", "timeout talking to matrix", sentAt: null);

        using var client = CreateClient();
        await LoginAndChangePasswordAsync(client, "matrix-health-passw0rd-123!");

        var response = await client.GetAsync("/api/admin/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal(HealthState.Degraded, health!.Matrix.State);
        Assert.Equal("delivery-degraded", health.Matrix.Status);
        Assert.Equal(1, health.Matrix.RecentSuccessCount);
        Assert.Equal(1, health.Matrix.RecentFailureCount);
        Assert.Equal(HealthState.Degraded, health.OverallHealth);
    }

    [Fact]
    public async Task AdminHealth_returns_503_when_recent_matrix_delivery_attempts_all_fail()
    {
        await SeedNotificationAsync("pending", "401 unauthorized", sentAt: null);

        using var client = CreateClient();
        await LoginAndChangePasswordAsync(client, "matrix-health-error-passw0rd-123!");

        var response = await client.GetAsync("/api/admin/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task LoginAndChangePasswordAsync(HttpClient client, string newPassword)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = newPassword });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    private async Task SeedNotificationAsync(string status, string? errorSummary, DateTimeOffset? sentAt)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO public.notifications (
                id,
                operation_id,
                ingestion_run_id,
                notification_type,
                provider,
                target,
                status,
                payload_json,
                rendered_body,
                message_summary,
                provider_message_id,
                attempt_count,
                next_retry_at,
                error_summary,
                sent_at,
                created_at,
                updated_at
            ) VALUES (
                @id,
                NULL,
                @ingestion_run_id,
                'ingestion_summary',
                'matrix',
                'matrix',
                @status,
                '{}'::jsonb,
                'rendered body',
                'summary',
                NULL,
                1,
                @next_retry_at,
                @error_summary,
                @sent_at,
                @created_at,
                @updated_at
            );
            """,
            connection);

        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("ingestion_run_id", Guid.NewGuid());
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("next_retry_at", status == "sent" ? DBNull.Value : now.AddMinutes(5));
        command.Parameters.AddWithValue("error_summary", (object?)errorSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("sent_at", (object?)sentAt ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", now.AddMinutes(-1));
        command.Parameters.AddWithValue("updated_at", now);

        await command.ExecuteNonQueryAsync();
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
            throw new InvalidOperationException("Docker did not return a container id for the admin health Matrix test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", ["rm", "-f", _containerName]);
        }
        catch
        {
            // Best-effort cleanup.
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL admin health Matrix test container to become ready after 90 seconds.");
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

    private sealed class AdminHealthMatrixWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
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
                    ["BOOTSTRAP_ADMIN_PASSWORD"] = BootstrapPassword,
                    ["notifications:matrix:enabled"] = "true",
                    ["notifications:matrix:homeserverUrl"] = "https://matrix.example.com",
                    ["notifications:matrix:accessToken"] = "secret-token",
                    ["notifications:matrix:roomId"] = "!room:example.com",
                    ["notifications:matrix:botUserId"] = "@digest:example.com"
                });
            });
        }
    }

    private sealed class CsrfTokenResponse
    {
        public string Token { get; set; } = string.Empty;
    }
}