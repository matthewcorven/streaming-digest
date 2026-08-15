namespace StreamingDigest.Web.Services;

public static class ApiBaseAddressResolver
{
    public static Uri ResolveConfiguredBaseAddress(
        string hostBaseAddress,
        string? serviceApiBaseAddress,
        string? configuredApiBaseAddress)
    {
        var selectedBaseAddress = serviceApiBaseAddress
            ?? configuredApiBaseAddress
            ?? hostBaseAddress;

        return Uri.TryCreate(selectedBaseAddress, UriKind.Absolute, out var absoluteBaseAddress)
            ? absoluteBaseAddress
            : new Uri(new Uri(hostBaseAddress, UriKind.Absolute), selectedBaseAddress);
    }

    public static async Task<Uri> ResolveRuntimeBaseAddressAsync(
        string hostBaseAddress,
        string? serviceApiBaseAddress,
        string? configuredApiBaseAddress,
        Func<Uri, CancellationToken, Task<bool>> sameOriginApiProbe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostBaseAddress);
        ArgumentNullException.ThrowIfNull(sameOriginApiProbe);

        if (!string.IsNullOrWhiteSpace(serviceApiBaseAddress))
        {
            return ResolveConfiguredBaseAddress(hostBaseAddress, serviceApiBaseAddress, configuredApiBaseAddress);
        }

        var hostBaseUri = new Uri(hostBaseAddress, UriKind.Absolute);
        var configuredBaseUri = ResolveConfiguredBaseAddress(hostBaseAddress, serviceApiBaseAddress: null, configuredApiBaseAddress);

        if (string.IsNullOrWhiteSpace(configuredApiBaseAddress) || IsSameOrigin(hostBaseUri, configuredBaseUri))
        {
            return configuredBaseUri;
        }

        try
        {
            if (await sameOriginApiProbe(hostBaseUri, cancellationToken))
            {
                return hostBaseUri;
            }
        }
        catch
        {
        }

        return configuredBaseUri;
    }

    public static async Task<bool> ProbeSameOriginApiAsync(Uri hostBaseUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostBaseUri);

        using var httpClient = new HttpClient
        {
            BaseAddress = hostBaseUri
        };

        using var response = await httpClient.GetAsync("/api/setup/status", cancellationToken);
        var payload = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        return IsSetupStatusResponse(response, payload);
    }

    public static bool IsSetupStatusResponse(HttpResponseMessage response, string? payload)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var mediaType = response.Content?.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(payload)
            && payload.Contains("setupRequired", StringComparison.Ordinal);
    }

    private static bool IsSameOrigin(Uri left, Uri right)
        => left.Scheme == right.Scheme
            && left.Host == right.Host
            && left.Port == right.Port;
}