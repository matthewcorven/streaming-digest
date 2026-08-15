using System.Net;
using System.Net.Http.Json;
using StreamingDigest.Web.Models;
using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class ModelStatusServiceTests
{
    [Fact]
    public async Task Verify_CamelCaseVerifiedPayload_TransitionsRowToReady()
    {
        using var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5149")
        };

        var authenticationService = new AuthenticationService(httpClient);
        var searchUiSessionService = new SearchUiSessionService(authenticationService);
        await using var service = new ModelStatusService(searchUiSessionService);
        var row = new ModelRowViewModel(
            id: "whisper",
            label: "Whisper Base",
            provider: "whisper",
            family: "audio",
            runtimeRole: "audio",
            downloadable: false);

        await service.Verify(row);

        Assert.Equal(ModelRowState.Ready, row.RowState);
        Assert.Null(row.ErrorMessage);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => Task.FromResult(JsonResponse(new { username = "admin", mustChangePassword = false })),
                "/api/auth/csrf" => Task.FromResult(JsonResponse(new { token = "csrf-token" })),
                "/api/models/verify" => Task.FromResult(JsonResponse(new
                {
                    status = "verified",
                    modelKind = "audio",
                    modelId = "whisper",
                    verified = true,
                    message = "Whisper service is reachable."
                })),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            };
        }

        private static HttpResponseMessage JsonResponse<T>(T payload)
            => new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
    }
}