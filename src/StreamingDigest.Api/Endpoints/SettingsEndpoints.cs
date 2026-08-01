using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Api.Endpoints;

internal static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app, string connectionString, ObservabilityRuntimeState observabilityRuntime, ILogger logger)
    {
        app.MapGet("/api/settings", (HttpContext context) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(new
                {
                    values = new Dictionary<string, object?>
                    {
                        ["observability.enabled"] = observabilityRuntime.Enabled,
                        ["observability.ready"] = observabilityRuntime.Ready,
                        ["observability.retentionDays"] = observabilityRuntime.RetentionDays,
                        ["observability.retentionWarning"] = observabilityRuntime.RetentionWarning,
                        ["observability.links.grafanaUrl"] = observabilityRuntime.GrafanaUrl,
                        ["observability.links.prometheusUrl"] = observabilityRuntime.PrometheusUrl,
                        ["observability.links.lokiUrl"] = observabilityRuntime.LokiUrl,
                        ["observability.links.tempoUrl"] = observabilityRuntime.TempoUrl,
                        ["observability.links.hangfireUrl"] = observabilityRuntime.HangfireUrl
                    }
                })
                : Results.Unauthorized());

        app.MapPut("/api/settings", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            if (!ApiRequestPipeline.HasCsrfToken(context.Request))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                var body = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(context.Request.Body, cancellationToken: cancellationToken);
                if (body is not null && body.TryGetValue("observability.enabled", out var enabledValue) && enabledValue.ValueKind == JsonValueKind.True)
                {
                    await ObservabilityEndpoints.PersistObservabilitySettingAsync(connectionString, logger, true, cancellationToken);
                    observabilityRuntime.Enabled = true;
                    await ObservabilityEndpoints.EnsureObservabilityReadinessAsync(logger, observabilityRuntime);
                }
                else if (body is not null && body.TryGetValue("observability.enabled", out enabledValue) && enabledValue.ValueKind == JsonValueKind.False)
                {
                    await ObservabilityEndpoints.PersistObservabilitySettingAsync(connectionString, logger, false, cancellationToken);
                    observabilityRuntime.Enabled = false;
                    observabilityRuntime.Ready = false;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process observability settings update");
            }

            return Results.Ok(new { status = "updated" });
        });
    }
}