using System.Net;
using System.Net.Http.Json;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;
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

    [Fact]
    public async Task InitializeAsync_preserves_setup_required_when_setup_status_cannot_be_reached()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("backend unavailable"));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var service = new AuthenticationService(httpClient);

        await service.InitializeAsync();

        Assert.True(service.IsSetupRequired);
        Assert.False(service.IsAuthenticated);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}

public sealed class FirstUserSetupServicePolicyTests
{
    [Fact]
    public void EvaluateSetupRequired_returns_true_when_no_users_exist()
    {
        Assert.True(FirstUserSetupService.EvaluateSetupRequired(userCount: 0, usersRequiringPasswordChange: 0, channelCount: 0));
    }

    [Fact]
    public void EvaluateSetupRequired_returns_true_when_only_bootstrap_users_exist_and_no_channels_exist()
    {
        Assert.True(FirstUserSetupService.EvaluateSetupRequired(userCount: 1, usersRequiringPasswordChange: 1, channelCount: 0));
    }

    [Fact]
    public void EvaluateSetupRequired_returns_false_when_channels_exist()
    {
        Assert.False(FirstUserSetupService.EvaluateSetupRequired(userCount: 1, usersRequiringPasswordChange: 1, channelCount: 1));
    }

    [Fact]
    public void EvaluateSetupRequired_returns_false_when_password_has_been_set()
    {
        Assert.False(FirstUserSetupService.EvaluateSetupRequired(userCount: 1, usersRequiringPasswordChange: 0, channelCount: 0));
    }
}
