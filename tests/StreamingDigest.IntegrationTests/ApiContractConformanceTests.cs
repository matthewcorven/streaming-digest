using System.Net;
using System.Net.Http.Headers;
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
        "POST /api/models/verify",
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

                var authenticatedWithoutCsrfResponse = await SendProbeAsync(client, entry, authenticated: true, includeCsrf: false);
                Assert.Equal(HttpStatusCode.Forbidden, authenticatedWithoutCsrfResponse.StatusCode);
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

        if (authenticated)
        {
            request.Headers.Add("X-Test-Auth", "true");
        }

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
