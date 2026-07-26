using StreamingDigest.Web.Models;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class ObservabilityLinkCatalogTests
{
    [Fact]
    public void Create_PrefersGrafanaForLokiAndTempoWhenGrafanaIsConfigured()
    {
        var links = ObservabilityLinkCatalog.Create(
            "/admin/jobs",
            "http://grafana:3000",
            "http://prometheus:9090",
            "http://loki:3100",
            "http://tempo:3200");

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("Hangfire", link.Label);
                Assert.Equal("/admin/jobs", link.Url);
            },
            link =>
            {
                Assert.Equal("Grafana", link.Label);
                Assert.Equal("/grafana", link.Url);
            },
            link =>
            {
                Assert.Equal("Prometheus", link.Label);
                Assert.Equal("/prometheus", link.Url);
            },
            link =>
            {
                Assert.Equal("Loki (via Grafana)", link.Label);
                Assert.Equal("/grafana/explore", link.Url);
            },
            link =>
            {
                Assert.Equal("Tempo (via Grafana)", link.Label);
                Assert.Equal("/grafana/explore", link.Url);
            });
    }

    [Fact]
    public void Create_FallsBackToDirectLokiAndTempoLinksWhenGrafanaIsNotConfigured()
    {
        var links = ObservabilityLinkCatalog.Create(
            "/admin/jobs",
            "",
            "",
            "http://loki:3100",
            "http://tempo:3200");

        Assert.Collection(
            links,
            link =>
            {
                Assert.Equal("Hangfire", link.Label);
                Assert.Equal("/admin/jobs", link.Url);
            },
            link =>
            {
                Assert.Equal("Loki", link.Label);
                Assert.Equal("/loki", link.Url);
            },
            link =>
            {
                Assert.Equal("Tempo", link.Label);
                Assert.Equal("/tempo", link.Url);
            });
    }

    [Fact]
    public void Create_SkipsServicesWithoutConfiguredUrls()
    {
        var links = ObservabilityLinkCatalog.Create(
            hangfireUrl: "",
            grafanaUrl: "http://grafana:3000",
            prometheusUrl: null,
            lokiUrl: "",
            tempoUrl: null);

        var linkLabels = links.Select(link => link.Label).ToArray();

        Assert.Equal(["Grafana"], linkLabels);
    }
}
