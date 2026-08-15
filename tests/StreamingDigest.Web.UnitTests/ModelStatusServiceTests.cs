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

    [Fact]
    public async Task Download_TransitionsRowToQueued()
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
            provider: "ollama",
            family: "audio",
            runtimeRole: "audio",
            downloadable: true);

        var success = await service.Download(row);

        Assert.True(success);
        Assert.Equal(ModelRowState.Queued, row.RowState);
        Assert.NotEqual(Guid.Empty, row.CurrentOperationId);
    }

    [Fact]
    public async Task Download_FailsWhenAlreadyDownloading()
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
            provider: "ollama",
            family: "audio",
            runtimeRole: "audio",
            downloadable: true);

        // First download succeeds
        var firstSuccess = await service.Download(row);
        Assert.True(firstSuccess);
        Assert.Equal(ModelRowState.Queued, row.RowState);

        // Second download fails (row is not in Unknown/DownloadFailed/VerifyFailed state)
        var secondSuccess = await service.Download(row);
        Assert.False(secondSuccess);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly object? _optionsPayload;
        private readonly object? _statusPayload;

        public StubHttpMessageHandler(object? optionsPayload = null, object? statusPayload = null)
        {
            _optionsPayload = optionsPayload;
            _statusPayload = statusPayload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => Task.FromResult(JsonResponse(new { username = "admin", mustChangePassword = false })),
                "/api/auth/csrf" => Task.FromResult(JsonResponse(new { token = "csrf-token" })),
                "/api/models/options" => Task.FromResult(JsonResponse(_optionsPayload ?? new { models = Array.Empty<object>() })),
                "/api/models/status" => Task.FromResult(JsonResponse(_statusPayload ?? new { models = Array.Empty<object>() })),
                "/api/models/verify" => Task.FromResult(JsonResponse(new
                {
                    status = "verified",
                    modelKind = "audio",
                    modelId = "whisper",
                    verified = true,
                    message = "Whisper service is reachable."
                })),
                "/api/models/download" => Task.FromResult(JsonResponse(new
                {
                    operationId = Guid.NewGuid(),
                    status = "Queued"
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