using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace StreamingDigest.IntegrationTests;

public sealed class ObservabilityApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ObservabilityApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetObservability_ReturnsHangfireAndGrafanaPreferredLinks()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/observability");
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(contentStream);

        var links = document.RootElement.GetProperty("links").EnumerateArray().ToArray();
        var labels = links.Select(link => link.GetProperty("label").GetString()!).ToArray();

        Assert.Equal(
            ["Hangfire", "Grafana", "pgAdmin", "Prometheus", "Loki (via Grafana)", "Tempo (via Grafana)"],
            labels);
        Assert.Equal("/admin/jobs", links[0].GetProperty("url").GetString());
        Assert.Equal("/grafana", links[1].GetProperty("url").GetString());
        Assert.Equal("/pgadmin", links[2].GetProperty("url").GetString());
        Assert.Equal("/prometheus", links[3].GetProperty("url").GetString());
        Assert.Equal("/grafana/explore", links[4].GetProperty("url").GetString());
        Assert.Equal("/grafana/explore", links[5].GetProperty("url").GetString());
    }
}
