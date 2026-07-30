using System.Net;
using System.Net.Http.Json;
using StreamingDigest.Application;
using StreamingDigest.Web.Services;

namespace StreamingDigest.UnitTests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task GetAuthenticatedJsonAsync_returns_the_payload_for_a_valid_session()
    {
        var handler = new StubHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.OK),
                "/api/auth/csrf" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { token = "csrf-123" })
                },
                "/api/search-ui/settings" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SearchUiSettings
                    {
                        TextWeight = 0.4,
                        VectorWeight = 0.6
                    })
                },
                _ => throw new InvalidOperationException($"Unexpected path: {request.RequestUri?.AbsolutePath}")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var service = new AuthenticationService(httpClient);

        var settings = await service.GetAuthenticatedJsonAsync<SearchUiSettings>("/api/search-ui/settings");

        Assert.NotNull(settings);
        Assert.Equal(0.4, settings!.TextWeight);
        Assert.Equal(0.6, settings.VectorWeight);
        Assert.True(service.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_tracks_password_change_required_state()
    {
        var handler = new StubHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/login" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { username = "admin", mustChangePassword = true })
                },
                _ => throw new InvalidOperationException($"Unexpected path: {request.RequestUri?.AbsolutePath}")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var service = new AuthenticationService(httpClient);

        var result = await service.LoginAsync("admin", "admin");

        Assert.Equal(AuthenticationService.LoginResult.RequiresPasswordChange, result);
        Assert.True(service.IsAuthenticated);
        Assert.True(service.RequiresPasswordChange);
    }

    [Fact]
    public async Task GetAuthenticatedJsonAsync_resets_auth_state_when_the_api_rejects_the_session()
    {
        var handler = new StubHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => new HttpResponseMessage(HttpStatusCode.OK),
                "/api/auth/csrf" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { token = "csrf-123" })
                },
                "/api/search-ui/settings" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => throw new InvalidOperationException($"Unexpected path: {request.RequestUri?.AbsolutePath}")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var service = new AuthenticationService(httpClient);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthenticatedJsonAsync<SearchUiSettings>("/api/search-ui/settings"));
        Assert.False(service.IsAuthenticated);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
