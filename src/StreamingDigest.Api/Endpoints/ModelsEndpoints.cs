extern alias StreamingDigestWorker;

using System.Text.Json;
using Hangfire;
using Hangfire.MemoryStorage;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
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

        app.MapPost("/api/models/download", async (HttpContext context, ModelDiscoveryService modelDiscoveryService, IModelRuntimeStateRepository stateRepository, IOperationStore operationStore, IBackgroundJobClient backgroundJobs, JobStorage hangfireStorage, CancellationToken cancellationToken) =>
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

            ModelOptionDefinition model;
            try
            {
                model = modelDiscoveryService.ResolveDownloadableModel(request?.ModelKind, request?.ModelId);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { title = "Unsupported model", detail = ex.Message });
            }

            // Durable handoff (WS-5): the 202 is only returned after the operation record and
            // the model_runtime_state=queued row are persisted AND the Hangfire job is enqueued.
            // When Hangfire falls back to in-memory storage (Postgres unavailable at startup),
            // the API process never runs a Hangfire server so an enqueued job could never execute;
            // reject explicitly instead of returning an optimistic 202 that cannot be fulfilled.
            // Uses the DI-registered storage for THIS app rather than the process-global
            // JobStorage.Current, which is shared across hosts (e.g. co-hosted test factories).
            if (hangfireStorage is MemoryStorage)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Download handoff unavailable",
                    detail: "The job queue is running on in-memory storage because PostgreSQL is unavailable; a model download cannot be durably persisted or executed right now.");
            }

            var provider = model.Provider.ToString().ToLowerInvariant();
            var runtimeRole = model.RuntimeRole.ToString().ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            var operationId = Guid.NewGuid();
            var statusUrl = $"/api/admin/operations/{operationId}";

            var operation = new OperationRecord
            {
                Id = operationId,
                OperationType = "model.download",
                Status = "queued",
                RequestedBy = context.User?.Identity?.Name,
                SummaryJson = JsonSerializer.Serialize(new
                {
                    provider,
                    modelId = model.Id,
                    runtimeRole,
                    requestedAt = now
                }),
                CreatedAt = now,
                UpdatedAt = now
            };

            var state = new ModelRuntimeState
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                ModelId = model.Id,
                RuntimeRole = runtimeRole,
                Status = ModelDownloadStatuses.Queued,
                CurrentOperationId = operationId,
                UpdatedAt = now
            };

            try
            {
                await operationStore.PersistAsync(operation, cancellationToken);
                await stateRepository.UpsertAsync(state, cancellationToken);

                var command = new ModelDownloadCommand(operationId, provider, model.Id, runtimeRole, now);
                var hangfireJobId = backgroundJobs.Enqueue<StreamingDigestWorker::StreamingDigest.Worker.ModelDownload.ModelDownloadJob>(job => job.RunAsync(command, CancellationToken.None));
                await operationStore.UpdateHangfireJobIdAsync(operationId, hangfireJobId, cancellationToken);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Download handoff failed",
                    detail: $"The download request could not be durably persisted and enqueued: {ex.Message}");
            }

            return Results.Accepted(statusUrl, new
            {
                status = "queued",
                operationId,
                statusUrl,
                modelKind = model.Family,
                modelId = model.Id
            });
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