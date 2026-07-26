namespace StreamingDigest.Web.Models;

public sealed class ObservabilityLinkDefinition
{
    public string Label { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public static class ObservabilityLinkCatalog
{
    public static IReadOnlyList<ObservabilityLinkDefinition> Create(
        string? hangfireUrl,
        string? grafanaUrl,
        string? prometheusUrl,
        string? lokiUrl,
        string? tempoUrl)
    {
        var links = new List<ObservabilityLinkDefinition>();
        var hasGrafana = HasConfiguredUrl(grafanaUrl);

        if (HasConfiguredUrl(hangfireUrl))
        {
            links.Add(new ObservabilityLinkDefinition
            {
                Label = "Hangfire",
                Url = hangfireUrl!,
                Description = "Background job dashboard"
            });
        }

        if (hasGrafana)
        {
            links.Add(new ObservabilityLinkDefinition
            {
                Label = "Grafana",
                Url = "/grafana",
                Description = "Dashboards and exploration"
            });
        }

        if (HasConfiguredUrl(prometheusUrl))
        {
            links.Add(new ObservabilityLinkDefinition
            {
                Label = "Prometheus",
                Url = "/prometheus",
                Description = "Metrics and scrape targets"
            });
        }

        if (HasConfiguredUrl(lokiUrl))
        {
            links.Add(new ObservabilityLinkDefinition
            {
                Label = hasGrafana ? "Loki (via Grafana)" : "Loki",
                Url = hasGrafana ? "/grafana/explore" : "/loki",
                Description = hasGrafana ? "Logs in Grafana Explore" : "Direct Loki access"
            });
        }

        if (HasConfiguredUrl(tempoUrl))
        {
            links.Add(new ObservabilityLinkDefinition
            {
                Label = hasGrafana ? "Tempo (via Grafana)" : "Tempo",
                Url = hasGrafana ? "/grafana/explore" : "/tempo",
                Description = hasGrafana ? "Traces in Grafana Explore" : "Direct Tempo access"
            });
        }

        return links;
    }

    private static bool HasConfiguredUrl(string? url)
        => !string.IsNullOrWhiteSpace(url);
}
