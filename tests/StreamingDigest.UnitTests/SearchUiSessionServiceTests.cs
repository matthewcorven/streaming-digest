using System.Net;
using System.Net.Http.Json;
using StreamingDigest.Application;
using StreamingDigest.Web.Services;

namespace StreamingDigest.UnitTests;

public sealed class SearchUiSessionServiceTests
{
    [Fact]
    public async Task EnsureAuthenticatedSessionAsync_uses_existing_session_and_csrf_endpoint()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri!.PathAndQuery switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.OK),
                "/api/auth/csrf" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { token = "csrf-token" })
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var authenticationService = new AuthenticationService(client);
        var service = new SearchUiSessionService(authenticationService);
        var token = await service.EnsureAuthenticatedSessionAsync();

        Assert.Equal("csrf-token", token);
        Assert.Equal(new[] { "/api/auth/me", "/api/auth/csrf" }, requests);
    }

    [Fact]
    public async Task EnsureAuthenticatedSessionAsync_throws_when_no_valid_session_exists()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            return request.RequestUri!.PathAndQuery switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var authenticationService = new AuthenticationService(client);
        var service = new SearchUiSessionService(authenticationService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.EnsureAuthenticatedSessionAsync());
    }

    [Fact]
    public async Task GetAuthenticatedJsonAsync_resets_auth_state_when_the_api_rejects_the_session()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri!.PathAndQuery switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.OK),
                "/api/auth/csrf" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { token = "csrf-token" })
                },
                "/api/search-ui/settings" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var authenticationService = new AuthenticationService(client);
        var service = new SearchUiSessionService(authenticationService);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthenticatedJsonAsync<SearchUiSettings>("/api/search-ui/settings"));

        Assert.False(authenticationService.IsAuthenticated);
        Assert.Equal(new[] { "/api/auth/me", "/api/auth/csrf", "/api/search-ui/settings" }, requests);
    }

    [Fact]
    public void RememberSelectedMode_tracks_the_latest_dashboard_or_search_route()
    {
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("http://localhost")
        };

        var service = new SearchUiSessionService(new AuthenticationService(client));

        service.RememberSelectedMode("/dashboard");
        Assert.Equal("/dashboard", service.GetLastSelectedModeRoute());

        service.RememberSelectedMode("/search");
        Assert.Equal("/search", service.GetLastSelectedModeRoute());
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request, cancellationToken));
    }
}
