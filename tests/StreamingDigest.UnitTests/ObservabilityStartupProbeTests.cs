using System.Net;
using System.Net.Http;
using StreamingDigest.Api.Observability;

namespace StreamingDigest.UnitTests;

public class ObservabilityStartupProbeTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsEmptyForHealthyTargets()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        var probe = new ObservabilityStartupProbe(client);

        var unreachableServices = await probe.ProbeAsync(new[]
        {
            (Name: "grafana", Url: "http://grafana:3000"),
            (Name: "prometheus", Url: "http://prometheus:9090")
        });

        Assert.Empty(unreachableServices);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsServiceNameForUnavailableTargets()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException());
        var client = new HttpClient(handler);
        var probe = new ObservabilityStartupProbe(client);

        var unreachableServices = await probe.ProbeAsync(new[]
        {
            (Name: "grafana", Url: "http://grafana:3000")
        });

        Assert.Equal(new[] { "grafana" }, unreachableServices);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
