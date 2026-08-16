using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StreamingDigest.Api.Services;

namespace StreamingDigest.Api.Endpoints;

/// <summary>
/// HTTP endpoint for health status server-sent events (SSE).
/// Clients connect to GET /api/health/stream to receive real-time status updates.
/// </summary>
internal static class HealthStreamingEndpoints
{
    public static void MapHealthStreamingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health/stream", GetHealthStream)
            .WithName("HealthStream")
            .Produces(StatusCodes.Status200OK)
            .WithSummary("Subscribe to live health status updates via SSE")
            .WithDescription("Establishes a server-sent events (SSE) stream for real-time health status updates. " +
                           "Initial connection receives a full status snapshot; subsequent updates are incremental. " +
                           "Reconnection auto-resumes from the same snapshot, guaranteeing no stale state on the client.");
    }

    private static async Task GetHealthStream(
        HealthStreamService healthStreamService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StreamingDigest.Api.Endpoints.HealthStreamingEndpoints");
        logger.LogDebug("SSE client connected: {ClientIp}", httpContext.Connection.RemoteIpAddress);

        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers.Append("Access-Control-Allow-Origin", "*");
        response.Headers.Append("Access-Control-Allow-Methods", "GET");

        try
        {
            using (var streamWriter = new StreamWriter(response.Body, leaveOpen: true))
            {
                streamWriter.AutoFlush = true;
                await healthStreamService.SubscribeAsync(streamWriter, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("SSE client disconnected (cancelled)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SSE stream: {ErrorMessage}", ex.Message);
        }
    }
}
