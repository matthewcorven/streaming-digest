using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;

namespace StreamingDigest.IntegrationTests;

public sealed class ApiContractConformanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public ApiContractConformanceTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
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
        using var client = _factory.CreateClient();

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
        using var client = _factory.CreateClient();
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
        using var client = _factory.CreateClient();
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
    }

    [Fact]
    public async Task ModelVerificationEndpoint_ReturnsVerifiedState()
    {
        using var client = _factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.True(loginResponse.IsSuccessStatusCode);

        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/models/verify");
        verifyRequest.Content = JsonContent.Create(new { modelKind = "embedding", modelId = "bge-m3" });
        using var verifyResponse = await client.SendAsync(verifyRequest);

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<ModelVerificationResponse>();
        Assert.NotNull(verifyPayload);
        Assert.True(verifyPayload.Verified);
        Assert.Equal("embedding", verifyPayload.ModelKind);
        Assert.Equal("bge-m3", verifyPayload.ModelId);
    }

    [Fact]
    public async Task PendingRouteList_MatchesCurrentHarnessExpectations()
    {
        using var client = _factory.CreateClient();
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
            "POST /api/models/verify",
            "POST /api/models/activate-embedding-model",
            "POST /api/models/activate-llm-model",
            "POST /api/models/activate-audio-model"
        };

        var actualOrdered = actualPendingRoutes.OrderBy(route => route, StringComparer.Ordinal).ToArray();
        var expectedOrdered = expectedPendingRoutes.OrderBy(route => route, StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedOrdered, actualOrdered);
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
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
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
    public string? Status { get; set; }
    public string? Label { get; set; }
}

public sealed class ModelDownloadResponse
{
    public string? Status { get; set; }
    public string? ModelKind { get; set; }
    public string? ModelId { get; set; }
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
