using StreamingDigest.Application.Admin;
using StreamingDigest.Application.Configuration;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Api.Endpoints;

internal static class AdminOperationsEndpoints
{
    public static void MapAdminOperationEndpoints(this WebApplication app, ApplicationConfiguration configuration, string contentRootPath)
    {
        app.MapGet("/api/admin/operations/{operationId:guid}", async (Guid operationId, IAdminOperationsService service, CancellationToken cancellationToken) =>
        {
            var operation = await service.GetOperationAsync(operationId, cancellationToken);
            return operation is null ? Results.NotFound() : Results.Ok(operation);
        });

        app.MapPost("/api/admin/operations/ingestion/run", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RunIngestionNowAsync(cancellationToken: cancellationToken)));
        app.MapPost("/api/admin/operations/ingestion/channel-backfill", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RunChannelBackfillAsync(cancellationToken: cancellationToken)));
        app.MapPost("/api/admin/operations/ingestion/runs/{runId}/retry", async (string runId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RetryFailedIngestionRunAsync(runId, cancellationToken)));
        app.MapPost("/api/admin/operations/videos/{videoId}/retry", async (string videoId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RetryFailedVideoAsync(videoId, cancellationToken)));
        app.MapPost("/api/admin/operations/links/{linkId}/retry", async (string linkId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RetryFailedLinkAsync(linkId, cancellationToken)));
        app.MapPost("/api/admin/operations/repositories/{repositoryId}/retry", async (string repositoryId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RetryFailedRepositoryAsync(repositoryId, cancellationToken)));
        app.MapPost("/api/admin/operations/videos/{videoId}/reprocess", async (string videoId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.ReprocessVideoAsync(videoId, cancellationToken)));
        app.MapPost("/api/admin/operations/repositories/{repositoryId}/reprocess", async (string repositoryId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.ReprocessRepositoryAsync(repositoryId, cancellationToken)));
        app.MapPost("/api/admin/operations/resources/{resourceId}/reprocess", async (string resourceId, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.ReprocessResourceAsync(resourceId, cancellationToken)));
        app.MapPost("/api/admin/operations/embeddings/reprocess", async (string? target, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.ReprocessEmbeddingsAsync(target, cancellationToken)));
        app.MapPost("/api/admin/operations/screenshots/purge", async (string? target, IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.PurgeScreenshotsAsync(target, cancellationToken)));
        app.MapPost("/api/admin/operations/notifications/matrix/test", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.TestMatrixNotificationAsync(cancellationToken)));
        app.MapPost("/api/admin/operations/embeddings/test", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.TestEmbeddingServiceAsync(cancellationToken)));
        app.MapPost("/api/admin/operations/audio-to-text/test", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.TestAudioToTextServiceAsync(cancellationToken)));
        app.MapPost("/api/admin/operations/backup", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.CreateBackupAsync(cancellationToken)));
        app.MapPost("/api/admin/operations/restore", async (IAdminOperationsService service, CancellationToken cancellationToken) =>
            CreateAdminOperationResponse(await service.RestoreLatestBackupAsync(cancellationToken)));
        app.MapGet("/api/admin/operations/backups/{archiveName}", (string archiveName) =>
        {
            if (archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return Results.BadRequest();
            }

            var backupDirectoryPath = ResolveBackupDirectoryPath(configuration, contentRootPath);
            var archivePath = Path.Combine(backupDirectoryPath, archiveName);
            return File.Exists(archivePath)
                ? Results.File(new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read), "application/zip", archiveName)
                : Results.NotFound();
        });

        app.MapGet("/api/internal/notifications/matrix/health", (IMatrixNotificationService service) => Results.Ok(new
        {
            status = "ok",
            enabled = service.IsEnabled
        }));

        app.MapPost("/api/internal/notifications/matrix/test", async (IMatrixNotificationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SendTestNotificationAsync(cancellationToken);
            return Results.Ok(new { success = result.Success, message = result.Message, responseBody = result.ResponseBody });
        });
    }

    private static IResult CreateAdminOperationResponse(AdminActionResult result)
    {
        if (result.Status is "completed" or "ok")
        {
            return Results.Ok(new
            {
                operationId = result.OperationId,
                operationType = result.OperationType,
                status = result.Status,
                message = result.Message,
                target = result.Target,
                healthStatus = result.HealthStatus
            });
        }

        if (result.Status is "failed" or "error")
        {
            return Results.Problem(detail: result.Message, statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Accepted($"/api/admin/operations/{result.OperationId}", new
        {
            operationId = result.OperationId,
            operationType = result.OperationType,
            status = result.Status,
            message = result.Message,
            target = result.Target,
            jobId = result.JobId
        });
    }

    private static string ResolveBackupDirectoryPath(ApplicationConfiguration configuration, string contentRootPath)
    {
        var configuredPath = configuration.Backup.DestinationPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(contentRootPath, "backups");
        }

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }
}