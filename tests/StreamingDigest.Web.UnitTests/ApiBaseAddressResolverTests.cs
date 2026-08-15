using Xunit;
using StreamingDigest.Web.Services;
using System.Net;
using System.Net.Http.Headers;

namespace StreamingDigest.Web.UnitTests;

public sealed class ApiBaseAddressResolverTests
{
    [Fact]
    public async Task ResolveRuntimeBaseAddressAsync_prefers_explicit_service_binding_without_probIng_same_origin()
    {
        var probeCalled = false;

        var resolved = await ApiBaseAddressResolver.ResolveRuntimeBaseAddressAsync(
            hostBaseAddress: "http://localhost:5001/",
            serviceApiBaseAddress: "http://localhost:6123/",
            configuredApiBaseAddress: "http://localhost:5149/",
            sameOriginApiProbe: (_, _) =>
            {
                probeCalled = true;
                return Task.FromResult(false);
            });

        Assert.False(probeCalled);
        Assert.Equal(new Uri("http://localhost:6123/"), resolved);
    }

    [Fact]
    public async Task ResolveRuntimeBaseAddressAsync_uses_host_origin_when_same_origin_api_probe_succeeds()
    {
        var resolved = await ApiBaseAddressResolver.ResolveRuntimeBaseAddressAsync(
            hostBaseAddress: "http://localhost:5001/",
            serviceApiBaseAddress: null,
            configuredApiBaseAddress: "http://localhost:5149",
            sameOriginApiProbe: (_, _) => Task.FromResult(true));

        Assert.Equal(new Uri("http://localhost:5001/"), resolved);
    }

    [Fact]
    public async Task ResolveRuntimeBaseAddressAsync_keeps_configured_origin_when_same_origin_api_probe_fails()
    {
        var resolved = await ApiBaseAddressResolver.ResolveRuntimeBaseAddressAsync(
            hostBaseAddress: "http://localhost:5001/",
            serviceApiBaseAddress: null,
            configuredApiBaseAddress: "http://localhost:5149",
            sameOriginApiProbe: (_, _) => Task.FromResult(false));

        Assert.Equal(new Uri("http://localhost:5149/"), resolved);
    }

    [Fact]
    public void IsSetupStatusResponse_rejects_html_fallback_documents()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>fallback</body></html>")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");

        Assert.False(ApiBaseAddressResolver.IsSetupStatusResponse(response, "<html><body>fallback</body></html>"));
    }

    [Fact]
    public void IsSetupStatusResponse_accepts_json_setup_status_payloads()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"setupRequired\":false}")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        Assert.True(ApiBaseAddressResolver.IsSetupStatusResponse(response, "{\"setupRequired\":false}"));
    }
}