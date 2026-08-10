using System.Text.Json;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.Api.Endpoints;

internal static class ModelsEndpoints
{
    public static void MapModelEndpoints(this WebApplication app, string connectionString, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/models/options", (HttpContext context, ModelDiscoveryService modelDiscoveryService) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(new
                {
                    models = modelDiscoveryService.GetSupportedModels().Select(model => new
                    {
                        id = model.Id,
                        family = model.Family,
                        provider = model.Provider.ToString().ToLowerInvariant(),
                        runtimeRole = model.RuntimeRole.ToString().ToLowerInvariant(),
                        downloadable = model.Downloadable,
                        status = model.Status,
                        label = model.Label,
                        installCommand = model.InstallCommand,
                        mountPath = model.MountPath
                    })
                })
                : Results.Unauthorized());

        app.MapPost("/api/models/download", async (HttpContext context, ModelDiscoveryService modelDiscoveryService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            ModelDiscoveryRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<ModelDiscoveryRequest>(context.Request.Body, jsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { title = "Invalid request", detail = "The model discovery request body could not be parsed." });
            }

            try
            {
                var result = await modelDiscoveryService.QueueDownloadAsync(connectionString, request?.ModelKind, request?.ModelId, cancellationToken);
                return Results.Accepted(result.StatusUrl, new
                {
                    status = result.Status,
                    operationId = result.OperationId,
                    statusUrl = result.StatusUrl,
                    modelKind = result.ModelKind,
                    modelId = result.ModelId
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { title = "Unsupported model", detail = ex.Message });
            }
        });

        app.MapPost("/api/models/verify", async (HttpContext context, ModelDiscoveryService modelDiscoveryService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            ModelDiscoveryRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<ModelDiscoveryRequest>(context.Request.Body, jsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { title = "Invalid request", detail = "The model verification request body could not be parsed." });
            }

            try
            {
                var result = await modelDiscoveryService.VerifyModelAsync(connectionString, request?.ModelKind, request?.ModelId, cancellationToken);
                return Results.Ok(new
                {
                    status = result.Status,
                    modelKind = result.ModelKind,
                    modelId = result.ModelId,
                    verified = result.Verified,
                    message = result.Message
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { title = "Unsupported model", detail = ex.Message });
            }
        });

        app.MapGet("/api/models/status", async (HttpContext context, IModelRuntimeStateRepository stateRepository, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            var states = await stateRepository.GetAllAsync(cancellationToken);
            return Results.Ok(new
            {
                models = states.Select(state => new
                {
                    provider = state.Provider,
                    modelId = state.ModelId,
                    runtimeRole = state.RuntimeRole,
                    status = state.Status,
                    currentOperationId = state.CurrentOperationId,
                    progressPercent = state.ProgressPercent,
                    lastVerifiedAt = state.LastVerifiedAt,
                    lastSeenInRuntimeAt = state.LastSeenInRuntimeAt,
                    lastErrorSummary = state.LastErrorSummary,
                    detailsJson = state.DetailsJson,
                    updatedAt = state.UpdatedAt
                })
            });
        });

        // SSE stream of model-lifecycle events. WASM-friendly native EventSource semantics:
        // plain GET, cookie auth via ApiRequestPipeline, no custom headers. Events come from
        // the in-process broadcaster fed by persisted state changes; the status endpoint above
        // remains authoritative for initial load and reconnect reconciliation (plan §6 / D5).
        app.MapGet("/api/models/events", async (HttpContext context, IModelLifecycleEventBroadcaster broadcaster) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            await context.Response.StartAsync(context.RequestAborted);

            // Send an initial comment so the client (and TestServer, which gates the response
            // on the first body bytes) observes the connection as established before any
            // model-lifecycle events are published.
            var stream = context.Response.Body;
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(": connected\n\n"), context.RequestAborted);
            await stream.FlushAsync(context.RequestAborted);

            await foreach (var modelEvent in broadcaster.Subscribe(context.RequestAborted))
            {
                var payload = $"event: {modelEvent.Name}\ndata: {modelEvent.DataJson}\n\n";
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(payload), context.RequestAborted);
                await stream.FlushAsync(context.RequestAborted);
            }
        });
    }
}

sealed record ModelDiscoveryRequest(string? ModelKind, string? ModelId);