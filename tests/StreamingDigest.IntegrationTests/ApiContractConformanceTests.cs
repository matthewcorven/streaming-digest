using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace StreamingDigest.IntegrationTests;

public sealed class ApiContractConformanceTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string BootstrapUsername = "admin";
    private const string BootstrapPassword = "admin";

    private readonly string _containerName = $"streaming-digest-contract-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private readonly ITestOutputHelper _output;
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    public ApiContractConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _factory = new ContractConformanceWebApplicationFactory(_connectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await StopPostgresContainerAsync();
    }

    private static readonly HashSet<string> KnownPendingRoutes = new(StringComparer.Ordinal)
    {
        "POST /api/models/activate-embedding-model",
        "POST /api/models/activate-llm-model",
        "POST /api/models/activate-audio-model"
    };

    public static IEnumerable<object[]> ContractCatalog()
    {
        return LoadMvpCatalog().Select(entry => new object[] { entry });
    }

    [Theory]
    [MemberData(nameof(ContractCatalog))]
    public async Task MvpCatalogEntries_AreImplementedOrTrackedAsPending(ApiContractCatalogEntry entry)
    {
        using var client = CreateClient();

        var unauthenticatedResponse = await SendProbeAsync(client, entry, authenticated: false, includeCsrf: false);
        var implemented = unauthenticatedResponse.StatusCode is not HttpStatusCode.NotFound and not HttpStatusCode.MethodNotAllowed;

        Assert.True(implemented || KnownPendingRoutes.Contains(entry.RouteKey), $"Expected {entry.RouteKey} to be implemented or tracked as pending.");

        if (!implemented)
        {
            return;
        }

        switch (entry.AuthBehavior)
        {
            case ContractAuthBehavior.Public:
                Assert.NotEqual(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
                Assert.NotEqual(HttpStatusCode.Forbidden, unauthenticatedResponse.StatusCode);
                break;
            case ContractAuthBehavior.Authenticated:
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
                break;
            case ContractAuthBehavior.AuthenticatedWithCsrf:
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);

               var authenticatedWithoutCsrfResponse = await SendProbeAsync(client, entry, authenticated: false, includeCsrf: false);
               Assert.Equal(HttpStatusCode.Unauthorized, authenticatedWithoutCsrfResponse.StatusCode);
                break;
        }
    }

    [Fact]
    public async Task MvpContractHarness_ReportsImplementedVsPendingCounts()
    {
        using var client = CreateClient();
        var catalog = LoadMvpCatalog();
        var implemented = 0;
        var pending = 0;

        foreach (var entry in catalog)
        {
            var response = await SendProbeAsync(client, entry, authenticated: false, includeCsrf: false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                pending++;
            }
            else
            {
                implemented++;
            }
        }

        _output.WriteLine($"API contract harness summary: implemented={implemented}/{catalog.Count} pending={pending}/{catalog.Count}");
        Assert.True(implemented > 0);
        Assert.True(pending > 0);
    }

    [Fact]
    public async Task ModelDiscoveryEndpoints_ReturnStructuredModelCatalogAndAcceptedDownloadState()
    {
        using var client = CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        var optionsResponse = await client.GetFromJsonAsync<ModelOptionsResponse>("/api/models/options");
        Assert.NotNull(optionsResponse);
        Assert.NotNull(optionsResponse.Models);
        Assert.Contains(optionsResponse.Models, model => model.Id == "llama3.1:8b" && model.Family == "llm");
        Assert.Contains(optionsResponse.Models, model => model.Id == "bge-m3" && model.Family == "embedding");

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/models/download");
        downloadRequest.Content = JsonContent.Create(new { modelKind = "llm", modelId = "llama3.1:8b" });
        using var downloadResponse = await client.SendAsync(downloadRequest);

        Assert.Equal(HttpStatusCode.Accepted, downloadResponse.StatusCode);
        var downloadPayload = await downloadResponse.Content.ReadFromJsonAsync<ModelDownloadResponse>();
        Assert.NotNull(downloadPayload);
        Assert.Equal("queued", downloadPayload.Status);
        Assert.Equal("llm", downloadPayload.ModelKind);
        Assert.Equal("llama3.1:8b", downloadPayload.ModelId);

        // WS-5 durable handoff: the operation row and model_runtime_state=queued row must
        // exist (with a hangfire_job_id) before the API returned 202.
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var operationCommand = new NpgsqlCommand(
            "SELECT status, operation_type, hangfire_job_id FROM public.operations WHERE id = @id",
            connection);
        operationCommand.Parameters.AddWithValue("id", downloadPayload.OperationId);
        await using (var reader = await operationCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync(), "Expected a persisted operations row for the download.");
            Assert.Equal("queued", reader.GetString(0));
            Assert.Equal("model.download", reader.GetString(1));
            Assert.False(reader.IsDBNull(2));
            Assert.False(string.IsNullOrWhiteSpace(reader.GetString(2)));
        }

        await using var stateCommand = new NpgsqlCommand(
            "SELECT status, current_operation_id FROM public.model_runtime_state WHERE provider = 'ollama' AND model_id = 'llama3.1:8b'",
            connection);
        await using (var reader = await stateCommand.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync(), "Expected a persisted model_runtime_state row for the download.");
            Assert.Equal("queued", reader.GetString(0));
            Assert.Equal(downloadPayload.OperationId, reader.GetGuid(1));
        }
    }

    [Fact]
    public async Task ModelDownloadEndpoint_RejectsVerifyOnlyModels()
    {
        using var client = CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/models/download");
        downloadRequest.Content = JsonContent.Create(new { modelKind = "embedding", modelId = "text-embedding-3-small" });
        using var downloadResponse = await client.SendAsync(downloadRequest);

        Assert.Equal(HttpStatusCode.BadRequest, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task ModelDownloadEndpoint_Returns503_WhenHangfireStorageIsInMemory()
    {
        // WS-5 durable handoff: when the API cannot reach Postgres for Hangfire storage it must
        // refuse the download with 503 rather than return an optimistic 202 whose job can never
        // execute (the API process runs no Hangfire server). Simulate by overriding the DI
        // JobStorage with MemoryStorage for this host only.
        using var factory = new ContractConformanceWebApplicationFactory(_connectionString!, useMemoryHangfireStorage: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/models/download");
        downloadRequest.Content = JsonContent.Create(new { modelKind = "llm", modelId = "llama3.1:8b" });
        using var downloadResponse = await client.SendAsync(downloadRequest);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, downloadResponse.StatusCode);
        var problem = await downloadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Download handoff unavailable", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ModelDiscoveryEndpoints_ReturnProviderAwareMetadata()
    {
        using var client = CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        var optionsResponse = await client.GetFromJsonAsync<ModelOptionsResponse>("/api/models/options");
        Assert.NotNull(optionsResponse);
        Assert.NotNull(optionsResponse.Models);

        var bgeModel = optionsResponse.Models.FirstOrDefault(m => m.Id == "bge-m3");
        Assert.NotNull(bgeModel);
        Assert.Equal("ollama", bgeModel.Provider);
        Assert.Equal("embedding", bgeModel.RuntimeRole);
        Assert.True(bgeModel.Downloadable);

        var textEmbedding3Small = optionsResponse.Models.FirstOrDefault(m => m.Id == "text-embedding-3-small");
        Assert.NotNull(textEmbedding3Small);
        Assert.Equal("openai", textEmbedding3Small.Provider);
        Assert.Equal("embedding", textEmbedding3Small.RuntimeRole);
        Assert.False(textEmbedding3Small.Downloadable);

        var whisper = optionsResponse.Models.FirstOrDefault(m => m.Id == "whisper");
        Assert.NotNull(whisper);
        Assert.Equal("whisper", whisper.Provider);
        Assert.Equal("audio", whisper.RuntimeRole);
        Assert.False(whisper.Downloadable);

        var llama = optionsResponse.Models.FirstOrDefault(m => m.Id == "llama3.1:8b");
        Assert.NotNull(llama);
        Assert.Equal("ollama", llama.Provider);
        Assert.Equal("llm", llama.RuntimeRole);
        Assert.True(llama.Downloadable);
    }

    [Fact]
    public async Task ModelVerificationEndpoint_WithoutReachableRuntime_ReportsHonestFailure()
    {
        // WS-4 (#203): verify runs a real runtime probe and must not report success when the
        // runtime is unreachable. The harness container has no Ollama, so the probe fails and
        // the response must be a truthful failure, not an optimistic "verified".
        using var client = CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/models/verify");
        verifyRequest.Content = JsonContent.Create(new { modelKind = "embedding", modelId = "bge-m3" });
        using var verifyResponse = await client.SendAsync(verifyRequest);

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<ModelVerificationResponse>();
        Assert.NotNull(verifyPayload);
        Assert.False(verifyPayload.Verified);
        Assert.Equal("failed", verifyPayload.Status);
        Assert.False(string.IsNullOrWhiteSpace(verifyPayload.Message));
        Assert.Equal("embedding", verifyPayload.ModelKind);
        Assert.Equal("bge-m3", verifyPayload.ModelId);
    }

    [Fact]
    public async Task PendingRouteList_MatchesCurrentHarnessExpectations()
    {
        using var client = CreateClient();
        var catalog = LoadMvpCatalog();
        var actualPendingRoutes = new List<string>();

        foreach (var entry in catalog)
        {
            var response = await SendProbeAsync(client, entry, authenticated: false, includeCsrf: false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                actualPendingRoutes.Add(entry.RouteKey);
            }
        }

        var expectedPendingRoutes = new[]
        {
            "POST /api/models/activate-embedding-model",
            "POST /api/models/activate-llm-model",
            "POST /api/models/activate-audio-model"
        };

        var actualOrdered = actualPendingRoutes.OrderBy(route => route, StringComparer.Ordinal).ToArray();
        var expectedOrdered = expectedPendingRoutes.OrderBy(route => route, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedOrdered, actualOrdered);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL contract test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL contract test container to become ready.");
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

    private sealed class ContractConformanceWebApplicationFactory(string connectionString, bool useMemoryHangfireStorage = false) : WebApplicationFactory<Program>
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
            if (useMemoryHangfireStorage)
            {
                builder.ConfigureServices(services =>
                {
                    // Replace ALL JobStorage registrations (Hangfire's + the app's explicit one)
                    // with in-memory storage so the endpoint's degraded-mode guard is exercised.
                    var descriptors = services.Where(d => d.ServiceType == typeof(Hangfire.JobStorage)).ToList();
                    foreach (var descriptor in descriptors)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton<Hangfire.JobStorage>(new Hangfire.MemoryStorage.MemoryStorage());
                });
            }
        }
    }

    private static IReadOnlyList<ApiContractCatalogEntry> LoadMvpCatalog()
    {
        var repoRoot = FindRepositoryRoot();
        var specPath = Path.Combine(repoRoot, "docs", "api", "API_SPEC.md");
        var lines = File.ReadAllLines(specPath);
        var entries = new List<ApiContractCatalogEntry>();

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*#{2,6}\s*(GET|POST|PUT|PATCH|DELETE)\s+`([^`]+)`", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            var method = match.Groups[1].Value.ToUpperInvariant();
            var route = match.Groups[2].Value;
            if (!ShouldIncludeInMvpCatalog(route))
            {
                continue;
            }

            entries.Add(new ApiContractCatalogEntry(method, route, ResolveAuthBehavior(method, route)));
        }

        return entries;
    }

    private static bool ShouldIncludeInMvpCatalog(string route)
    {
        return route.StartsWith("/api/auth/", StringComparison.Ordinal)
            || route.StartsWith("/api/onboarding/", StringComparison.Ordinal)
            || route.StartsWith("/api/settings", StringComparison.Ordinal)
            || route.StartsWith("/api/config/", StringComparison.Ordinal)
            || route.StartsWith("/api/models/", StringComparison.Ordinal);
    }

    private static ContractAuthBehavior ResolveAuthBehavior(string method, string route)
    {
        if (route.StartsWith("/api/auth/login", StringComparison.Ordinal)
            || route.StartsWith("/api/auth/logout", StringComparison.Ordinal)
            || route.StartsWith("/api/auth/csrf", StringComparison.Ordinal))
        {
            return ContractAuthBehavior.Public;
        }

        if (route.StartsWith("/api/auth/change-password", StringComparison.Ordinal))
        {
            return ContractAuthBehavior.AuthenticatedWithCsrf;
        }

        if (route.StartsWith("/api/auth/", StringComparison.Ordinal))
        {
            return ContractAuthBehavior.Authenticated;
        }

        return ContractAuthBehavior.Authenticated;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "api", "API_SPEC.md");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the repository root containing docs/api/API_SPEC.md.");
    }

    private static async Task<HttpResponseMessage> SendProbeAsync(HttpClient client, ApiContractCatalogEntry entry, bool authenticated, bool includeCsrf)
    {
        var requestUri = SubstituteRouteParameters(entry.Path);
        using var request = new HttpRequestMessage(new HttpMethod(entry.Method), requestUri);

        if (includeCsrf)
        {
            request.Headers.Add("X-CSRF-Token", "test-token");
        }

        if (!request.Method.Equals(HttpMethod.Get))
        {
            request.Content = CreateProbeContent(entry.Path);
        }

        return await client.SendAsync(request);
    }

    private static StringContent CreateProbeContent(string path)
    {
        if (path.StartsWith("/api/auth/login", StringComparison.Ordinal))
        {
            return new StringContent("{\"username\":\"admin\",\"password\":\"admin\"}", Encoding.UTF8, "application/json");
        }

        return new StringContent("{}", Encoding.UTF8, "application/json");
    }

    private static string SubstituteRouteParameters(string path)
    {
        return Regex.Replace(path, @"\{[^}]+\}", "sample", RegexOptions.CultureInvariant);
    }
}

public sealed class ModelOptionsResponse
{
    public ModelOption[]? Models { get; set; }
}

public sealed class ModelOption
{
    public string? Id { get; set; }
    public string? Family { get; set; }
    public string? Provider { get; set; }
    public string? RuntimeRole { get; set; }
    public bool Downloadable { get; set; }
    public string? Status { get; set; }
    public string? Label { get; set; }
}

public sealed class ModelDownloadResponse
{
    public string? Status { get; set; }
    public string? ModelKind { get; set; }
    public string? ModelId { get; set; }
    public Guid OperationId { get; set; }
    public string? StatusUrl { get; set; }
}

public sealed class ModelVerificationResponse
{
    public string? Status { get; set; }
    public string? ModelKind { get; set; }
    public string? ModelId { get; set; }
    public bool Verified { get; set; }
    public string? Message { get; set; }
}

public sealed record ApiContractCatalogEntry(string Method, string Path, ContractAuthBehavior AuthBehavior)
{
    public string RouteKey => $"{Method.ToUpperInvariant()} {Path}";
}

public enum ContractAuthBehavior
{
    Public,
    Authenticated,
    AuthenticatedWithCsrf
}
