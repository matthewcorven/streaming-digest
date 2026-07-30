using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

[Collection("AuthFlow")]
public sealed class AuthFlowIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";

    private readonly string _containerName = $"streaming-digest-auth-tests-{Guid.NewGuid():N}";
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

        _factory = new AuthFlowWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Login_sets_cookies_and_blocks_protected_routes_until_password_is_changed()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        Assert.True(loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, cookie => cookie.Contains("auth-session=", StringComparison.Ordinal));

        var protectedResponse = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Forbidden, protectedResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.NotNull(me);
        Assert.Equal(BootstrapUsername, me!.Username);
        Assert.True(me.MustChangePassword);
    }

    [Fact]
    public async Task Logout_clears_the_session_and_rejects_follow_up_requests()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Console.WriteLine(loginBody);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_allows_relogin_with_the_new_password()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        Console.WriteLine(loginBody);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = "new-passw0rd-123!" });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = "new-passw0rd-123!" });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.NotNull(me);
        Assert.Equal(BootstrapUsername, me!.Username);
        Assert.False(me.MustChangePassword);
    }

    [Fact]
    public async Task Observability_status_endpoint_remains_public()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/observability");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("mode", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_routes_require_authentication()
    {
        using var client = CreateClient();

        var routes = new[]
        {
            "/api/settings",
            "/api/config/runtime",
            "/api/internal/notifications/matrix/health"
        };

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Search_endpoint_returns_one_cluster_per_video_and_uses_the_effective_title()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = "search-passw0rd-123!" });
        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = "search-passw0rd-123!" });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);

        var searchCsrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, searchCsrfResponse.StatusCode);
        var searchCsrf = await searchCsrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(searchCsrf);

        using var searchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/search-ui/search");
        searchRequest.Headers.Add("X-CSRF-Token", searchCsrf!.Token);
        searchRequest.Content = JsonContent.Create(new
        {
            query = "project idea search",
            filters = new
            {
                resultType = "video"
            }
        });

        var searchResponse = await client.SendAsync(searchRequest);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var payload = await searchResponse.Content.ReadFromJsonAsync<SearchUiApiResponse>();
        Assert.NotNull(payload);

        var cluster = Assert.Single(payload!.Results, result => result.ClusterId == "cluster-search-ui");
        Assert.Equal("Designing a search-first knowledge base", cluster.Title);
        Assert.Equal(4, cluster.MatchesInsideCount);
        Assert.Equal(2, cluster.Submatches.Count(match => string.Equals(match.Type, "segment", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Authenticated_mutating_requests_without_csrf_tokens_are_rejected()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings");
        request.Content = JsonContent.Create(new Dictionary<string, object?> { ["observability.enabled"] = true });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hangfire_dashboard_requires_authentication_for_unauthenticated_clients()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/admin/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hangfire_dashboard_is_available_to_authenticated_clients()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = "new-passw0rd-123!" });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = "new-passw0rd-123!" });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);

        var response = await client.GetAsync("/admin/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hangfire", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disallowed_internal_and_media_paths_are_not_served_by_the_spa_fallback()
    {
        using var client = CreateClient();

        var mediaResponse = await client.GetAsync("/api/screenshots/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, mediaResponse.StatusCode);

        var traversalResponse = await client.GetAsync("/api/screenshots/../../index.html");
        Assert.Equal(HttpStatusCode.NotFound, traversalResponse.StatusCode);
    }

    [Fact]
    public async Task Runtime_configuration_endpoint_does_not_expose_sensitive_settings()
    {
        using var client = CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);

        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrf!.Token);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = "new-passw0rd-123!" });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = "new-passw0rd-123!" });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);

        var response = await client.GetAsync("/api/config/runtime");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionstrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mutating_request_without_a_session_is_rejected()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings");
        request.Content = JsonContent.Create(new Dictionary<string, object?> { ["observability.enabled"] = true });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL auth integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL auth integration test container to become ready.");
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

    private sealed class AuthFlowWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
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

    private sealed record AuthMeResponse(string Username, bool MustChangePassword);

    private sealed record CsrfTokenResponse(string Token);

    private sealed class SearchUiApiResponse
    {
        public List<SearchUiClusterResponse> Results { get; set; } = new();
    }

    private sealed class SearchUiClusterResponse
    {
        public string ClusterId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int MatchesInsideCount { get; set; }

        public List<SearchUiSubmatchResponse> Submatches { get; set; } = new();
    }

    private sealed class SearchUiSubmatchResponse
    {
        public string Type { get; set; } = string.Empty;
    }
}
