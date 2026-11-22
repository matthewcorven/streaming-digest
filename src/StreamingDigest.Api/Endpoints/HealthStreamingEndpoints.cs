using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Api.Services;
using System.Text;

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
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("StreamingDigest.Api.Endpoints.HealthStreamingEndpoints");
        logger.LogDebug("SSE client connected: {ClientIp}", httpContext.Connection.RemoteIpAddress);

        if (!ApiRequestPipeline.IsAuthenticated(httpContext.Request))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var response = httpContext.Response;
        
        // Restrict CORS to configured allowed origins
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
            ?? new[] { "http://localhost:3000", "http://localhost:5149" };
        var origin = httpContext.Request.Headers["Origin"].FirstOrDefault() ?? "";

        if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
        {
            response.Headers.Append("Access-Control-Allow-Origin", origin);
            logger.LogDebug("CORS origin allowed: {Origin}", origin);
        }
        else if (!string.IsNullOrEmpty(origin))
        {
            logger.LogWarning("CORS origin rejected: {Origin}", origin);
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.Headers.Append("Access-Control-Allow-Methods", "GET");

        try
        {
            await response.StartAsync(cancellationToken);

            // Send an initial comment so the client and TestServer both observe the stream as
            // established before the snapshot payload is written.
            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(": connected\n\n"), cancellationToken);
            await response.Body.FlushAsync(cancellationToken);

            await healthStreamService.SendInitialSnapshotAsync(response.Body, cancellationToken);

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
