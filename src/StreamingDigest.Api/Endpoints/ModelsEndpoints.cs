using System.Text.Json;
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
    }
}

sealed record ModelDiscoveryRequest(string? ModelKind, string? ModelId);