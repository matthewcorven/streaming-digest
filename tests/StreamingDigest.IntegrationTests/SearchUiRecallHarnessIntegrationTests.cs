using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.IntegrationTests;

public sealed class SearchUiRecallHarnessIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "bootstrap-admin";
    private const string BootstrapPassword = "s3cr3t-passw0rd";
    private const string NewPassword = "new-passw0rd-123!";

    private readonly string _containerName = $"streaming-digest-search-recall-tests-{Guid.NewGuid():N}";
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

        _factory = new RecallHarnessWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task SearchUi_api_keeps_each_golden_query_in_the_top_three()
    {
        using var client = CreateClient();
        await AuthenticateAsync(client);

        foreach (var query in LoadGoldenQueries())
        {
            using var response = await client.PostAsJsonAsync("/api/search-ui/search", new
            {
                query = query.Query,
                filters = new
                {
                    resultType = "video"
                }
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<SearchResponsePayload>();
            Assert.NotNull(payload);

            var topThree = payload!.Results.Take(3).Select(result => result.ClusterId).ToArray();
            Assert.Contains(query.ExpectedClusterId, topThree);
        }
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = BootstrapPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrfToken = await GetCsrfTokenAsync(client);
        using var changePasswordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePasswordRequest.Headers.Add("X-CSRF-Token", csrfToken);
        changePasswordRequest.Content = JsonContent.Create(new { currentPassword = BootstrapPassword, newPassword = NewPassword });

        var changePasswordResponse = await client.SendAsync(changePasswordRequest);
        Assert.Equal(HttpStatusCode.OK, changePasswordResponse.StatusCode);

        var reloginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = BootstrapUsername, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);
    }

    private async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(csrf);
        return csrf!.Token;
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

    private static List<GoldenQuery> LoadGoldenQueries()
    {
        var path = ResolveRepoFile("tests/Fixtures/recall/vague-query-corpus.json");
        return JsonSerializer.Deserialize<GoldenQueryDataset>(File.ReadAllText(path))!.Queries;
    }

    private static string ResolveRepoFile(string relativePath)
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            var candidate = Path.Combine(currentDirectory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            currentDirectory = currentDirectory.Parent;
        }

        var workingCandidate = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
        if (File.Exists(workingCandidate))
        {
            return workingCandidate;
        }

        throw new FileNotFoundException($"Could not resolve repository file '{relativePath}'.", relativePath);
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run", "--rm", "-d", "--name", _containerName,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL search recall integration test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL search recall integration test container to become ready.");
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

    private sealed class RecallHarnessWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
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

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SearchUiService>();
                services.AddSingleton(new SearchUiService(SearchRecallRepresentativeCorpusFactory.CreateRepresentativeCorpus()));
            });
        }
    }

    private sealed class GoldenQueryDataset
    {
        [JsonPropertyName("queries")]
        public List<GoldenQuery> Queries { get; set; } = [];
    }

    private sealed class GoldenQuery
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("expected_cluster_id")]
        public string ExpectedClusterId { get; set; } = string.Empty;
    }

    private sealed class SearchResponsePayload
    {
        [JsonPropertyName("results")]
        public List<SearchResultPayload> Results { get; set; } = [];
    }

    private sealed class SearchResultPayload
    {
        [JsonPropertyName("clusterId")]
        public string ClusterId { get; set; } = string.Empty;
    }

    private sealed record CsrfTokenResponse(string Token);
}
