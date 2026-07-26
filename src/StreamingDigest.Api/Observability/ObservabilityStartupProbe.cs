using System.Net;

namespace StreamingDigest.Api.Observability;

public sealed class ObservabilityStartupProbe
{
    private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromMilliseconds(750);
    private readonly HttpClient _httpClient;

    public ObservabilityStartupProbe(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<IReadOnlyList<string>> ProbeAsync(IEnumerable<(string Name, string Url)> probeTargets, CancellationToken cancellationToken = default)
    {
        var unreachableServices = new List<string>();

        foreach (var (name, url) in probeTargets)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                unreachableServices.Add(name);
                continue;
            }

            try
            {
                var probeUri = new UriBuilder(url)
                {
                    Path = "/",
                    Query = string.Empty
                }.Uri;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(StartupProbeTimeout);

                using var response = await _httpClient.GetAsync(probeUri, timeoutCts.Token);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                unreachableServices.Add(name);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                unreachableServices.Add(name);
            }
            catch (Exception)
            {
                unreachableServices.Add(name);
            }
        }

        return unreachableServices;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            Proxy = null,
            UseProxy = false
        };

        return new HttpClient(handler)
        {
            Timeout = StartupProbeTimeout
        };
    }
}
