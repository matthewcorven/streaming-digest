using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Api.Observability;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Endpoints;

internal static class ObservabilityEndpoints
{
    public static void MapObservabilityEndpoints(this WebApplication app, string connectionString, ObservabilityRuntimeState observabilityRuntime, ILogger logger)
    {
        app.MapGet("/api/observability", () => Results.Ok(new
        {
            enabled = observabilityRuntime.Enabled,
            ready = observabilityRuntime.Ready,
            mode = observabilityRuntime.Enabled ? "enabled" : "disabled",
            retentionDays = observabilityRuntime.RetentionDays,
            retentionWarning = observabilityRuntime.RetentionWarning,
            links = ObservabilityLinkCatalog.Create(
                observabilityRuntime.HangfireUrl,
                observabilityRuntime.GrafanaUrl,
                observabilityRuntime.PgAdminUrl,
                observabilityRuntime.PrometheusUrl,
                observabilityRuntime.LokiUrl,
                observabilityRuntime.TempoUrl),
            services = new
            {
                hangfire = observabilityRuntime.HangfireUrl,
                grafana = observabilityRuntime.GrafanaUrl,
                pgadmin = observabilityRuntime.PgAdminUrl,
                prometheus = observabilityRuntime.PrometheusUrl,
                loki = observabilityRuntime.LokiUrl,
                tempo = observabilityRuntime.TempoUrl,
                otelCollector = observabilityRuntime.OtelCollectorUrl
            }
        }));

        app.MapPost("/api/observability/toggle/{enabled:bool}", async (bool enabled, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            await PersistObservabilitySettingAsync(connectionString, logger, enabled, cancellationToken);
            observabilityRuntime.Enabled = enabled;
            if (enabled)
            {
                await EnsureObservabilityReadinessAsync(logger, observabilityRuntime);
            }
            else
            {
                observabilityRuntime.Ready = false;
            }

            return Results.Ok(new { enabled, mode = enabled ? "enabled" : "disabled" });
        });

        app.MapMethods("/grafana", ["GET", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
        {
            if (context.Request.Path.Equals("/grafana/", StringComparison.OrdinalIgnoreCase))
            {
                return await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "grafana", observabilityRuntime.GrafanaUrl, cancellationToken);
            }

            var target = string.Concat("/grafana/", context.Request.QueryString.Value);
            return Results.Redirect(target, permanent: false);
        });

        app.MapMethods("/grafana/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "grafana", observabilityRuntime.GrafanaUrl, cancellationToken));
        app.MapMethods("/pgadmin/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "pgadmin", observabilityRuntime.PgAdminUrl, cancellationToken));
        app.MapMethods("/prometheus/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "prometheus", observabilityRuntime.PrometheusUrl, cancellationToken));
        app.MapMethods("/loki/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "loki", observabilityRuntime.LokiUrl, cancellationToken));
        app.MapMethods("/tempo/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
            await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "tempo", observabilityRuntime.TempoUrl, cancellationToken));
    }

    public static bool ResolveDefaultObservabilityEnabled(IConfiguration configuration, IHostEnvironment environment, bool fallbackEnabled)
    {
        if (bool.TryParse(configuration["observability:enabled"], out var configuredEnabled))
        {
            return configuredEnabled;
        }

        var overrideValue = Environment.GetEnvironmentVariable("STREAMINGDIGEST_OBSERVABILITY_ENABLED");
        if (bool.TryParse(overrideValue, out var environmentEnabled))
        {
            return environmentEnabled;
        }

        return environment.IsDevelopment() || IsLocalhostUrl(configuration) ? true : fallbackEnabled;
    }

    public static async Task<ObservabilityRuntimeState> LoadObservabilityRuntimeAsync(ILogger logger, IConfiguration configuration, IHostEnvironment environment, string connectionString, bool databaseConnected, bool defaultEnabled, int defaultRetentionDays)
    {
        var resolvedEnabled = ResolveDefaultObservabilityEnabled(configuration, environment, defaultEnabled);
        var resolvedRetentionDays = defaultRetentionDays;
        var grafanaUrl = configuration["observability:services:grafana:url"] ?? "http://grafana:3000";
        var pgAdminUrl = configuration["observability:services:pgadmin:url"] ?? "http://pgadmin:5050";
        var prometheusUrl = configuration["observability:services:prometheus:url"] ?? "http://prometheus:9090";
        var lokiUrl = configuration["observability:services:loki:url"] ?? "http://loki:3100";
        var tempoUrl = configuration["observability:services:tempo:url"] ?? "http://tempo:3200";
        var hangfireUrl = configuration["observability:services:hangfire:url"] ?? "/admin/jobs";
        var otelCollectorUrl = configuration["observability:services:otelCollector:url"] ?? "http://otel-collector:4317";

        if (databaseConnected)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                resolvedEnabled = await ReadBoolSettingAsync(connection, "observability.enabled", resolvedEnabled);
                resolvedRetentionDays = await ReadIntSettingAsync(connection, "observability.retentionDays", resolvedRetentionDays);
                grafanaUrl = await ReadStringSettingAsync(connection, "observability.links.grafanaUrl", grafanaUrl);
                pgAdminUrl = await ReadStringSettingAsync(connection, "observability.links.pgadminUrl", pgAdminUrl);
                prometheusUrl = await ReadStringSettingAsync(connection, "observability.links.prometheusUrl", prometheusUrl);
                lokiUrl = await ReadStringSettingAsync(connection, "observability.links.lokiUrl", lokiUrl);
                tempoUrl = await ReadStringSettingAsync(connection, "observability.links.tempoUrl", tempoUrl);
                hangfireUrl = await ReadStringSettingAsync(connection, "observability.links.hangfireUrl", hangfireUrl);
                otelCollectorUrl = await ReadStringSettingAsync(connection, "observability.links.otelCollectorUrl", otelCollectorUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not hydrate observability settings from PostgreSQL; falling back to configuration defaults");
            }
        }

        return new ObservabilityRuntimeState
        {
            Enabled = resolvedEnabled,
            RetentionDays = resolvedRetentionDays,
            RetentionWarning = resolvedRetentionDays <= 0,
            GrafanaUrl = grafanaUrl,
            PgAdminUrl = pgAdminUrl,
            PrometheusUrl = prometheusUrl,
            LokiUrl = lokiUrl,
            TempoUrl = tempoUrl,
            HangfireUrl = hangfireUrl,
            OtelCollectorUrl = otelCollectorUrl
        };
    }

    public static async Task EnsureObservabilityReadinessAsync(ILogger logger, ObservabilityRuntimeState observabilityRuntime)
    {
        if (!observabilityRuntime.Enabled)
        {
            observabilityRuntime.Ready = false;
            return;
        }

        try
        {
            var probeTargets = new[]
            {
                (Name: "grafana", Url: observabilityRuntime.GrafanaUrl),
                (Name: "pgadmin", Url: observabilityRuntime.PgAdminUrl),
                (Name: "prometheus", Url: observabilityRuntime.PrometheusUrl),
                (Name: "loki", Url: observabilityRuntime.LokiUrl),
                (Name: "tempo", Url: observabilityRuntime.TempoUrl)
            };

            var startupProbe = new ObservabilityStartupProbe();
            var unreachableServices = await startupProbe.ProbeAsync(probeTargets);
            if (unreachableServices.Count == 0)
            {
                observabilityRuntime.Ready = true;
                return;
            }

            observabilityRuntime.Ready = false;
            logger.LogWarning("Observability services are not yet reachable; links will remain hidden until startup completes. Unreachable services: {Services}", string.Join(", ", unreachableServices));
        }
        catch (Exception ex)
        {
            observabilityRuntime.Ready = false;
            logger.LogWarning(ex, "Observability services are not yet reachable; links will remain hidden until startup completes");
        }
    }

    public static async Task PersistObservabilitySettingAsync(string connectionString, ILogger logger, bool enabled, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("INSERT INTO public.app_settings (key, value_json, updated_at) VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP) ON CONFLICT (key) DO UPDATE SET value_json = EXCLUDED.value_json, updated_at = CURRENT_TIMESTAMP", connection);
            command.Parameters.AddWithValue("key", "observability.enabled");
            command.Parameters.AddWithValue("valueJson", JsonSerializer.Serialize(enabled));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist observability.enabled");
        }
    }

    private static bool IsLocalhostUrl(IConfiguration configuration)
    {
        var urls = string.Join(";", new[]
        {
            configuration["urls"],
            configuration["ASPNETCORE_URLS"],
            Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
            Environment.GetEnvironmentVariable("urls")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return urls.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || urls.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || urls.Contains("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ReadBoolSettingAsync(NpgsqlConnection connection, string key, bool fallback)
    {
        return await AppSettingReader.ReadBoolAsync(connection, key, fallback);
    }

    private static async Task<int> ReadIntSettingAsync(NpgsqlConnection connection, string key, int fallback)
    {
        return await AppSettingReader.ReadIntAsync(connection, key, fallback);
    }

    private static async Task<string> ReadStringSettingAsync(NpgsqlConnection connection, string key, string fallback)
    {
        return await AppSettingReader.ReadStringAsync(connection, key, fallback);
    }

    private static async Task<IResult> ProxyObservabilityRequestAsync(HttpContext context, IHttpClientFactory httpClientFactory, ObservabilityRuntimeState observabilityRuntime, string serviceName, string upstreamBaseUrl, CancellationToken cancellationToken)
    {
        if (!observabilityRuntime.Enabled)
        {
            return Results.Content(BuildDisabledPlaceholder(serviceName), "text/html", Encoding.UTF8);
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            var targetUri = BuildProxyUri(context.Request.Path, serviceName, upstreamBaseUrl, context.Request.QueryString.Value);
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
            var forwardedHost = context.Request.Host.HasValue ? context.Request.Host.Value : null;

            if (ShouldForwardRequestBody(context.Request))
            {
                var requestBodyBytes = await ReadRequestBodyAsync(context.Request, cancellationToken);
                if (requestBodyBytes.Length > 0)
                {
                    request.Content = new ByteArrayContent(requestBodyBytes);
                    if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                    {
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(forwardedHost))
            {
                request.Headers.Host = forwardedHost;
                request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);
            }

            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", $"/{serviceName}");

            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("host", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            context.Response.StatusCode = (int)response.StatusCode;
            CopyProxyResponseHeaders(context.Response, response);

            if (!string.IsNullOrWhiteSpace(contentType))
            {
                context.Response.ContentType = contentType;
            }

            await context.Response.Body.WriteAsync(responseBytes, cancellationToken);
            return Results.Empty;
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static void CopyProxyResponseHeaders(HttpResponse destination, HttpResponseMessage source)
    {
        foreach (var header in source.Headers)
        {
            if (header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in source.Content.Headers)
        {
            if (header.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Headers[header.Key] = header.Value.ToArray();
        }
    }

    private static bool ShouldForwardRequestBody(HttpRequest request)
    {
        return request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.Body.CanSeek)
        {
            request.EnableBuffering();
        }

        request.Body.Position = 0;
        await using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;
        return buffer.ToArray();
    }

    private static Uri BuildProxyUri(PathString requestPath, string serviceName, string upstreamBaseUrl, string? query)
    {
        var baseUri = new Uri(upstreamBaseUrl.EndsWith('/') ? upstreamBaseUrl : upstreamBaseUrl + "/");
        var requestPathValue = requestPath.Value ?? string.Empty;
        var servicePrefix = $"/{serviceName}";
        var relativePath = serviceName.Equals("grafana", StringComparison.OrdinalIgnoreCase)
            ? requestPathValue
            : requestPathValue.StartsWith(servicePrefix, StringComparison.OrdinalIgnoreCase)
                ? requestPathValue[servicePrefix.Length..]
                : requestPathValue;

        var sanitizedPath = string.IsNullOrWhiteSpace(relativePath) ? "/" : relativePath;
        var builder = new UriBuilder(baseUri)
        {
            Path = sanitizedPath,
            Query = query?.TrimStart('?') ?? string.Empty
        };

        return builder.Uri;
    }

    private static string BuildDisabledPlaceholder(string serviceName)
    {
        return $"""
<!DOCTYPE html>
<html lang=\"en\">
<head>
  <meta charset=\"utf-8\" />
  <title>{serviceName} unavailable</title>
</head>
<body>
  <h1>{serviceName} is temporarily unavailable</h1>
  <p>Observability is currently disabled for this deployment. Re-enable it from the settings UI or by toggling the observability flag in the API.</p>
  <p>Once enabled, this route will proxy to the real {serviceName} service.</p>
</body>
</html>
""";
    }
}

public sealed class ObservabilityRuntimeState
{
    public bool Enabled { get; set; }
    public bool Ready { get; set; }
    public int RetentionDays { get; set; }
    public bool RetentionWarning { get; set; }
    public string GrafanaUrl { get; set; } = string.Empty;
    public string PgAdminUrl { get; set; } = string.Empty;
    public string PrometheusUrl { get; set; } = string.Empty;
    public string LokiUrl { get; set; } = string.Empty;
    public string TempoUrl { get; set; } = string.Empty;
    public string HangfireUrl { get; set; } = string.Empty;
    public string OtelCollectorUrl { get; set; } = string.Empty;
}