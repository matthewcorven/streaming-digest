using System.Net;
using System.Net.Http.Json;
using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task SendAuthenticatedRequestAsync_GetRequest_DoesNotAddCsrfHeader()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5149")
        };

        var service = new AuthenticationService(httpClient);
        await service.EnsureApiSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/models/events");
        using var response = await service.SendAuthenticatedRequestAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(handler.Requests.Single(r => r.RequestUri?.AbsolutePath == "/api/models/events").Headers, h => h.Key == "X-CSRF-Token");
        Assert.Equal(new Uri("http://localhost:5149"), service.ApiBaseAddress);
    }

    [Fact]
    public async Task SendAuthenticatedRequestAsync_PostRequest_AddsCsrfHeader()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5149")
        };

        var service = new AuthenticationService(httpClient);
        await service.EnsureApiSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/models/verify")
        {
            Content = JsonContent.Create(new { modelKind = "audio", modelId = "whisper" })
        };
        using var response = await service.SendAuthenticatedRequestAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(handler.Requests.Single(r => r.RequestUri?.AbsolutePath == "/api/models/verify").Headers, h => h.Key == "X-CSRF-Token");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));

            return request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => Task.FromResult(Json(HttpStatusCode.OK, new { username = "admin", mustChangePassword = false })),
                "/api/auth/csrf" => Task.FromResult(Json(HttpStatusCode.OK, new { token = "csrf-token" })),
                "/api/models/events" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(": connected\n\n")
                }),
                "/api/models/verify" => Task.FromResult(Json(HttpStatusCode.OK, new { status = "verified", verified = true, message = "ok" })),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            };
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T payload)
            => new(statusCode)
            {
                Content = JsonContent.Create(payload)
            };

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}