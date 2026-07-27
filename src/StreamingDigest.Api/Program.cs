using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StreamingDigest.Api.Observability;
using StreamingDigest.Application;
using StreamingDigest.Application.Admin;
using StreamingDigest.Domain;
using StreamingDigest.Application.Observability;
using Npgsql;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.MatrixNotifier;
using StreamingDigest.Web.Models;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var builder = WebApplication.CreateBuilder(args);

var applicationConfiguration = ApplicationConfigurationLoader.LoadFromDirectory(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(applicationConfiguration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.AddOtlpExporter(options => ConfigureOtlpExporter(options));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddSource(CorrelationContext.ActivitySourceName);
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddOtlpExporter(options => ConfigureOtlpExporter(options));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CorrelationContext.ActivitySourceName);
        metrics.AddOtlpExporter(options => ConfigureOtlpExporter(options));
    });

GlobalJobFilters.Filters.Add(new HangfireObservabilityFilter());

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<MatrixNotificationClient>();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new MatrixNotificationOptions
    {
        IsEnabled = configuration.GetValue<bool>("notifications:matrix:enabled"),
        HomeserverBaseUrl = configuration["notifications:matrix:homeserverUrl"] ?? "https://matrix-client.matrix.org",
        AccessToken = configuration["notifications:matrix:accessToken"] ?? string.Empty,
        RoomId = configuration["notifications:matrix:roomId"] ?? string.Empty,
        BotUserId = configuration["notifications:matrix:botUserId"],
        OnManualRuns = configuration.GetValue<bool>("notifications:matrix:onManualRuns"),
        OnScheduledRuns = configuration.GetValue<bool>("notifications:matrix:onScheduledRuns"),
        OnBackfillRuns = configuration.GetValue<bool>("notifications:matrix:onBackfillRuns"),
        DashboardBaseUrl = configuration["notifications:matrix:dashboardBaseUrl"] ?? "http://localhost:8080"
    };
});
builder.Services.AddSingleton<IMatrixNotificationService, MatrixNotificationService>();

var connectionString = builder.Configuration.GetConnectionString("streamingdigest")
    ?? builder.Configuration.GetConnectionString("postgres")
    ?? applicationConfiguration.ConnectionStrings.StreamingDigest;

var startupLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
});
var startupLogger = startupLoggerFactory.CreateLogger("Startup");
var databaseStatus = await EnsureDatabaseConnectivityAsync(startupLogger, connectionString);
var hangfireStorage = CreateHangfireStorage(startupLogger, connectionString, databaseStatus.Connected);

if (!databaseStatus.Connected)
{
    startupLogger.LogWarning("Hangfire PostgreSQL connectivity is unavailable; the dashboard will use in-memory storage for this startup.");
}

builder.Services.AddHangfire(config => config.UseStorage(hangfireStorage));
builder.Services.AddDbContext<StreamingDigestDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<BootstrapAdminUserService>();
builder.Services.AddSingleton<AppAuthService>();
builder.Services.AddSingleton<AppReadinessStateService>();
builder.Services.AddSingleton<ModelDiscoveryService>();
builder.Services.AddSingleton<IEffectiveValueService, EffectiveValueService>();
builder.Services.AddScoped<IAdminOperationStore, EfCoreAdminOperationStore>();
builder.Services.AddScoped<IAdminOperationsService>(sp => new AdminOperationsService(applicationConfiguration, builder.Environment.ContentRootPath, sp.GetRequiredService<IAdminOperationStore>()));
builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
builder.Services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();

var app = builder.Build();
var authService = app.Services.GetRequiredService<AppAuthService>();

static IResult CreateAdminOperationResponse(AdminActionResult result)
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

static string ResolveBackupDirectoryPath(ApplicationConfiguration configuration, string contentRootPath)
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

static string ResolveChannelName(string youtubeChannelId, string? sourceUrl)
{
    if (!string.IsNullOrWhiteSpace(sourceUrl))
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var lastSegment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastSegment) && !lastSegment.Equals("channel", StringComparison.OrdinalIgnoreCase))
            {
                return lastSegment;
            }
        }
    }

    return string.IsNullOrWhiteSpace(youtubeChannelId) ? "Channel" : youtubeChannelId;
}

static string ResolveChannelProfileUrl(string youtubeChannelId, string? sourceUrl)
{
    if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
    {
        return uri.ToString();
    }

    return string.IsNullOrWhiteSpace(youtubeChannelId)
        ? "https://www.youtube.com"
        : $"https://www.youtube.com/channel/{youtubeChannelId}";
}

static string ResolveYoutubeChannelId(string? sourceUrl)
{
    if (string.IsNullOrWhiteSpace(sourceUrl))
    {
        return string.Empty;
    }

    if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
    {
        return string.Empty;
    }

    if (!uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
    {
        return string.Empty;
    }

    var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length == 0)
    {
        return string.Empty;
    }

    if (segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
    {
        return segments[1];
    }

    if (segments[0].Equals("user", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
    {
        return segments[1];
    }

    if (segments[0].StartsWith('@'))
    {
        return segments[0].TrimStart('@');
    }

    return string.Empty;
}

static string ResolveChannelSourceUrl(string? sourceUrl)
{
    if (string.IsNullOrWhiteSpace(sourceUrl))
    {
        return string.Empty;
    }

    return sourceUrl.Trim();
}

static string ResolveChannelFallbackId(string? sourceUrl)
{
    if (string.IsNullOrWhiteSpace(sourceUrl))
    {
        return "channel";
    }

    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl));
    return $"channel-{Convert.ToHexString(bytes)[..12].ToLowerInvariant()}";
}

static ChannelValueResponse CreateValueResponse(IEffectiveValueService effectiveValueService, string? original, string? overrideValue)
{
    var resolvedValue = effectiveValueService.Resolve(original, overrideValue);
    return new(resolvedValue.Original, resolvedValue.Override, resolvedValue.Effective);
}

static string ResolveChannelDisplayName(EffectiveValue resolvedNameValue, string youtubeChannelId)
    => string.IsNullOrWhiteSpace(resolvedNameValue.Effective) ? youtubeChannelId : resolvedNameValue.Effective;

static ChannelListItemResponse MapChannelListItem(IEffectiveValueService effectiveValueService, Channel channel)
{
    var resolvedNameValue = effectiveValueService.Resolve(channel.NameOriginal, channel.NameOverride);
    return new(channel.Id, channel.YoutubeChannelId, ResolveChannelDisplayName(resolvedNameValue, channel.YoutubeChannelId), channel.ProfileUrl, channel.IsPaused, channel.IsDegraded, channel.ConsecutiveFailures, channel.LastIngestedAt, channel.LastIngestionStatus);
}

static ChannelDetailResponse MapChannelDetail(IEffectiveValueService effectiveValueService, Channel channel)
    => new(
        channel.Id,
        channel.YoutubeChannelId,
        CreateValueResponse(effectiveValueService, channel.NameOriginal, channel.NameOverride),
        CreateValueResponse(effectiveValueService, channel.DescriptionOriginal, channel.DescriptionOverride),
        channel.ProfileUrl,
        channel.SourceUrl,
        channel.IsPaused,
        channel.IsDegraded,
        channel.ConsecutiveFailures,
        channel.LastIngestedAt,
        channel.LastIngestionStatus,
        new ChannelIngestionDefaultsResponse(channel.DefaultMaxAgeDays, channel.DefaultBackfillMaxVideos));

static string CreateRunTitle(IngestionRun run)
{
    var runType = string.IsNullOrWhiteSpace(run.RunType) ? "Ingestion" : char.ToUpperInvariant(run.RunType[0]) + run.RunType[1..];
    return $"{runType} run #{run.Id.ToString("N")[..8]}";
}

static string CreateRunSubtitle(IngestionRun run)
    => $"{run.StartedAt.ToLocalTime():MMM d, yyyy h:mm tt} • {run.RunType}";

static string ToStatusText(string status, DateTimeOffset? completedAt)
{
    var normalizedStatus = status?.Trim().ToLowerInvariant() ?? "unknown";
    if (normalizedStatus is "completed" or "done" && completedAt is not null)
    {
        return "Completed";
    }

    return normalizedStatus switch
    {
        "failed" => "Failed",
        "deferred" => "Deferred",
        "pending" => "Pending",
        "in_progress" => "In progress",
        "completed_with_warnings" => "Completed with warnings",
        "processed_with_warnings" => "Processed with warnings",
        _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status
    };
}

static bool IsProcessedStatus(string status)
{
    var normalized = status?.Trim().ToLowerInvariant() ?? string.Empty;
    return normalized is "processed" or "done" or "completed" or "processed_with_warnings" or "completed_with_warnings";
}
using var startupScope = CorrelationContext.BeginLoggingScope(app.Logger, new Dictionary<string, object?>
{
    ["startup"] = "api",
    ["environment"] = app.Environment.EnvironmentName
});

var defaultObservabilityEnabled = ResolveDefaultObservabilityEnabled(builder.Configuration, builder.Environment, false);
var defaultRetentionDays = StorageRetentionPolicy.ComputeObservabilityRetentionDays(StorageRetentionPolicy.GetMaximumReadyDriveFreeSpaceBytes());
var observabilityRuntime = await LoadObservabilityRuntimeAsync(app.Logger, builder.Configuration, builder.Environment, connectionString, databaseStatus.Connected, defaultObservabilityEnabled, defaultRetentionDays);
await EnsureObservabilityReadinessAsync(app.Logger, observabilityRuntime);

if (databaseStatus.Connected)
{
    var compatibilityStateService = new UpgradeCompatibilityStateService(app.Logger);
    var compatibilityStateBeforeMigration = await compatibilityStateService.ReadVersionStateAsync(connectionString);
    var migrationRunner = new PostgresMigrationRunner(connectionString);
    await migrationRunner.ApplyAsync();

    var seeder = new AppSettingsSeeder(app.Logger);
    await seeder.SeedDefaultsAsync(connectionString, observabilityRuntime.Enabled, observabilityRuntime.RetentionDays);
    await compatibilityStateService.PersistRuntimeStateAsync(connectionString, applicationConfiguration, PostgresMigrationRunner.CurrentSchemaVersion);
    var compatibilityStateAfterMigration = await compatibilityStateService.ReadVersionStateAsync(connectionString);
    var compatibilityEvaluation = compatibilityStateService.Evaluate(
        applicationConfiguration,
        compatibilityStateBeforeMigration,
        compatibilityStateAfterMigration,
        PostgresMigrationRunner.CurrentSchemaVersion);

    app.Logger.LogInformation(
        "Upgrade compatibility category {Category} ({RiskLevel}). Compose tag {ComposeTag}. Backup recommended={BackupRecommended}; backup required={BackupRequired}",
        compatibilityEvaluation.Category,
        compatibilityEvaluation.RiskLevel,
        compatibilityEvaluation.ComposeTag,
        compatibilityEvaluation.BackupRecommended,
        compatibilityEvaluation.BackupRequired);

    var bootstrapAdminUserService = app.Services.GetRequiredService<BootstrapAdminUserService>();
    await bootstrapAdminUserService.EnsureBootstrapAdminUserAsync(connectionString);
    await authService.EnsureSchemaAsync(connectionString);

    var readinessStateService = app.Services.GetRequiredService<AppReadinessStateService>();
    await readinessStateService.EnsureSchemaAsync(connectionString);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (!RequiresAuthentication(context.Request))
    {
        await next();
        return;
    }

    RequestAuthenticationResult authenticationResult;
    try
    {
        authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Authentication middleware failed while validating the request session.");
        authenticationResult = new RequestAuthenticationResult(false, null, null, false);
    }

    if (!authenticationResult.IsAuthenticated)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (authenticationResult.RequiresPasswordChange && !ShouldAllowPasswordChangeFlow(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (RequiresCsrfProtection(context.Request) && !HasValidCsrfToken(context.Request, authenticationResult.CsrfToken))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Items["AuthenticatedUser"] = authenticationResult.User;
    await next();
});

app.UseHangfireDashboard("/admin/jobs", new DashboardOptions
{
    Authorization = new[] { new PassThroughDashboardAuthorizationFilter() }
});

app.MapGet("/api/channels", async (HttpContext context, StreamingDigestDbContext dbContext, IEffectiveValueService effectiveValueService) =>
{
    var query = context.Request.Query;
    var includePaused = bool.TryParse(query["includePaused"], out var includePausedValue) && includePausedValue;
    var page = int.TryParse(query["page"], out var pageValue) && pageValue > 0 ? pageValue : 1;
    var pageSize = int.TryParse(query["pageSize"], out var pageSizeValue) && pageSizeValue > 0 ? Math.Min(pageSizeValue, 100) : 25;

    var channelsQuery = dbContext.Channels.AsNoTracking().Where(channel => includePaused || !channel.IsPaused);
    var totalCount = await channelsQuery.CountAsync();

    var channels = await channelsQuery
        .OrderBy(channel => channel.NameOverride ?? channel.NameOriginal ?? channel.YoutubeChannelId)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var items = channels.Select(channel => MapChannelListItem(effectiveValueService, channel)).ToList();
    return Results.Ok(new { items, page, pageSize, totalCount });
});

app.MapPost("/api/channels", async (CreateChannelRequest request, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
{
    if (string.IsNullOrWhiteSpace(request.SourceUrl))
    {
        return Results.BadRequest(new { error = "sourceUrl is required." });
    }

    var youtubeChannelId = ResolveYoutubeChannelId(request.SourceUrl);
    if (string.IsNullOrWhiteSpace(youtubeChannelId))
    {
        youtubeChannelId = ResolveChannelFallbackId(request.SourceUrl);
    }

    var existingChannel = await channelRepository.GetByYoutubeChannelIdAsync(youtubeChannelId);
    if (existingChannel is not null)
    {
        return Results.Conflict(new { error = "A channel with the supplied YouTube channel id already exists." });
    }

    var resolvedSourceUrl = ResolveChannelSourceUrl(request.SourceUrl);
    var channel = new Channel
    {
        YoutubeChannelId = youtubeChannelId,
        NameOriginal = ResolveChannelName(youtubeChannelId, resolvedSourceUrl),
        ProfileUrl = ResolveChannelProfileUrl(youtubeChannelId, resolvedSourceUrl),
        SourceUrl = resolvedSourceUrl,
        DefaultMaxAgeDays = request.DefaultMaxAgeDays,
        DefaultBackfillMaxVideos = request.DefaultBackfillMaxVideos
    };

    await channelRepository.AddAsync(channel);
    return Results.Created($"/api/channels/{channel.Id}", MapChannelDetail(effectiveValueService, channel));
});

app.MapGet("/api/channels/{channelId:guid}", async (Guid channelId, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
{
    var channel = await channelRepository.GetByIdAsync(channelId);
    return channel is null ? Results.NotFound() : Results.Ok(MapChannelDetail(effectiveValueService, channel));
});

app.MapPut("/api/channels/{channelId:guid}", async (Guid channelId, UpdateChannelRequest request, IChannelRepository channelRepository, IEffectiveValueService effectiveValueService) =>
{
    var channel = await channelRepository.GetByIdAsync(channelId);
    if (channel is null)
    {
        return Results.NotFound();
    }

    if (request.NameOverride is not null)
    {
        channel.NameOverride = request.NameOverride;
    }

    if (request.DescriptionOverride is not null)
    {
        channel.DescriptionOverride = request.DescriptionOverride;
    }

    if (request.IsPaused is not null)
    {
        channel.IsPaused = request.IsPaused.Value;
    }

    if (request.DefaultMaxAgeDays is not null)
    {
        channel.DefaultMaxAgeDays = request.DefaultMaxAgeDays.Value;
    }

    if (request.DefaultBackfillMaxVideos is not null)
    {
        channel.DefaultBackfillMaxVideos = request.DefaultBackfillMaxVideos.Value;
    }

    await channelRepository.UpdateAsync(channel);
    return Results.Ok(new { status = "updated", entityType = "channel", entityId = channel.Id, resource = MapChannelDetail(effectiveValueService, channel) });
});

app.MapDelete("/api/channels/{channelId:guid}", async (Guid channelId, HttpContext context, IChannelRepository channelRepository) =>
{
    var deleteRelatedData = bool.TryParse(context.Request.Query["deleteRelatedData"], out var deleteRelatedValue) && deleteRelatedValue;
    var confirm = context.Request.Query["confirm"].ToString();

    var channel = await channelRepository.GetByIdAsync(channelId);
    if (channel is null)
    {
        return Results.NotFound();
    }

    if (deleteRelatedData && !string.Equals(confirm, "true", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "confirm=true is required when deleteRelatedData is requested." });
    }

    await channelRepository.DeleteAsync(channelId, purgeMedia: deleteRelatedData);
    return Results.Ok(new { status = "deleted", entityType = "channel", entityId = channelId });
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = databaseStatus.Connected ? "ok" : "degraded",
    database = databaseStatus.Connected ? "connected" : "disconnected",
    databaseName = databaseStatus.DatabaseName,
    serverVersion = databaseStatus.ServerVersion
}));
app.MapGet("/api/db-health", () => Results.Ok(new
{
    status = databaseStatus.Connected ? "connected" : "disconnected",
    database = databaseStatus.DatabaseName,
    serverVersion = databaseStatus.ServerVersion
}));
app.MapGet("/api/overview", () => Results.Ok(new { service = "streaming-digest-api", status = "ready" }));
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
app.MapGet("/api/admin/operations/backups/{archiveName}", (string archiveName, ApplicationConfiguration configuration) =>
{
    if (archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        return Results.BadRequest();
    }

    var backupDirectoryPath = ResolveBackupDirectoryPath(configuration, builder.Environment.ContentRootPath);
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
app.MapGet("/api/internal/ingestion-runs", async (int? limit, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 25, 1, 200);
    var runs = await context.IngestionRuns
        .OrderByDescending(run => run.StartedAt)
        .Take(take)
        .ToListAsync(cancellationToken);

    var response = runs.Select(run => new IngestionRunFixtureSummary
    {
        Id = run.Id.ToString(),
        Title = CreateRunTitle(run),
        Subtitle = CreateRunSubtitle(run),
        StatusText = ToStatusText(run.Status, run.CompletedAt)
    }).ToList();

    return Results.Ok(response);
});
app.MapGet("/api/internal/ingestion-runs/{ingestionRunId:guid}", async (Guid ingestionRunId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var run = await context.IngestionRuns
        .Where(candidate => candidate.Id == ingestionRunId)
        .SingleOrDefaultAsync(cancellationToken);

    if (run is null)
    {
        return Results.NotFound();
    }

    var items = await context.IngestionItems
        .Where(item => item.IngestionRunId == ingestionRunId)
        .OrderBy(item => item.CreatedAt)
        .ToListAsync(cancellationToken);

    var videoIds = items
        .Where(item => item.ItemId.HasValue && item.ItemType.Equals("video", StringComparison.OrdinalIgnoreCase))
        .Select(item => item.ItemId!.Value)
        .Distinct()
        .ToList();

    var videos = await context.Videos
        .Where(video => videoIds.Contains(video.Id))
        .Select(video => new
        {
            video.Id,
            Title = video.Title,
            ChannelName = video.Channel != null
                ? (video.Channel.NameOverride ?? video.Channel.NameOriginal)
                : "Unknown channel",
            video.TranscriptStatus,
            video.ScreenshotStatus,
            video.IngestionStatus
        })
        .ToDictionaryAsync(video => video.Id, cancellationToken);

    var now = DateTimeOffset.UtcNow;
    var stageRollups = items
        .GroupBy(item => item.Stage)
        .Select(group =>
        {
            var statuses = group.Select(item => item.Status?.ToLowerInvariant() ?? string.Empty).ToList();
            var stageStatus = statuses.Any(status => status == "failed") ? "warning"
                : statuses.Any(status => status == "deferred") ? "deferred"
                : statuses.Any(status => status == "pending") ? "pending"
                : "done";

            return new IngestionRunStageViewModel
            {
                Name = group.Key,
                Completed = group.Count(item => IsProcessedStatus(item.Status)),
                Total = group.Count(),
                Status = stageStatus,
                Detail = $"{group.Count(item => item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))} failed, {group.Count(item => item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase))} deferred."
            };
        })
        .OrderBy(stage => stage.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var perItemLinkCounts = items
        .Where(item => item.ItemType.Equals("link", StringComparison.OrdinalIgnoreCase) && item.ItemId.HasValue)
        .GroupBy(item => item.ItemId!.Value)
        .ToDictionary(group => group.Key, group => group.Count());
    var perItemRepositoryCounts = items
        .Where(item => item.ItemType.StartsWith("repository", StringComparison.OrdinalIgnoreCase) && item.ItemId.HasValue)
        .GroupBy(item => item.ItemId!.Value)
        .ToDictionary(group => group.Key, group => group.Count());
    var perItemWebsiteCounts = items
        .Where(item => (item.ItemType.StartsWith("website", StringComparison.OrdinalIgnoreCase) || item.ItemType.Equals("resource", StringComparison.OrdinalIgnoreCase)) && item.ItemId.HasValue)
        .GroupBy(item => item.ItemId!.Value)
        .ToDictionary(group => group.Key, group => group.Count());

    var mappedItems = items
        .Where(item => item.ItemType is not ("link" or "repository" or "resource"))
        .Select(item =>
        {
            var effectiveId = item.ItemId ?? item.Id;
            videos.TryGetValue(item.ItemId ?? Guid.Empty, out var video);
            var hasVideoData = video is not null;
            var title = hasVideoData ? video!.Title : (!string.IsNullOrWhiteSpace(item.ExternalKey) ? item.ExternalKey : $"{item.ItemType} {effectiveId.ToString("N")[..8]}");
            var itemStatus = ToStatusText(item.Status, item.CompletedAt);
            var retryHistory = new List<IngestionRunRetryEventViewModel>();
            if (item.RetryCount > 0)
            {
                retryHistory.Add(new IngestionRunRetryEventViewModel
                {
                    Label = $"Retries: {item.RetryCount}",
                    Detail = $"Attempt {item.Attempt} of {item.MaxAttempts}"
                });
            }

            return new IngestionRunItemViewModel
            {
                Id = effectiveId.ToString(),
                Title = title,
                Channel = hasVideoData ? video!.ChannelName : "Unknown channel",
                Status = itemStatus.ToLowerInvariant(),
                CanRetry = item.IsRetryable && item.Status is "failed" or "deferred",
                FailureSummary = item.ErrorSummary,
                Stage = item.Stage,
                TranscriptStatus = hasVideoData ? video!.TranscriptStatus : "unknown",
                ScreenshotStatus = hasVideoData ? video!.ScreenshotStatus : "unknown",
                EmbeddingStatus = hasVideoData ? video!.IngestionStatus : "unknown",
                LinkCount = perItemLinkCounts.GetValueOrDefault(effectiveId),
                RepositoryCount = perItemRepositoryCounts.GetValueOrDefault(effectiveId),
                WebsiteCount = perItemWebsiteCounts.GetValueOrDefault(effectiveId),
                RetryHistory = retryHistory
            };
        })
        .ToList();

    var deferments = items
        .Where(item => item.DeferredUntil.HasValue && (item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase) || item.DeferredUntil > now))
        .Select(item => new IngestionRunDefermentViewModel
        {
            Scope = string.IsNullOrWhiteSpace(item.ExternalKey) ? item.Stage : item.ExternalKey!,
            Reason = string.IsNullOrWhiteSpace(item.DefermentReason) ? "Rate-limit deferment is active." : item.DefermentReason!,
            ResumeLabel = item.DeferredUntil.HasValue ? $"Resumes {item.DeferredUntil.Value.ToLocalTime():h:mm tt}" : "Resume time unknown",
            IsActive = item.DeferredUntil > now || item.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase)
        })
        .ToList();

    var viewModel = new IngestionRunDetailViewModel
    {
        Id = run.Id.ToString(),
        Title = CreateRunTitle(run),
        Subtitle = CreateRunSubtitle(run),
        StatusText = ToStatusText(run.Status, run.CompletedAt),
        Description = $"Run type: {run.RunType}. Triggered by {run.TriggeredBy}.",
        IsCompleted = run.CompletedAt.HasValue,
        DefermentBanner = deferments.Count > 0 ? "One or more ingestion items are currently deferred." : null,
        FrozenOutcome = new IngestionRunOutcomeViewModel
        {
            Heading = "Frozen run outcome",
            Caption = run.CompletedAt.HasValue
                ? $"Captured at {run.CompletedAt.Value.ToLocalTime():g}."
                : "Run has not completed yet.",
            Channels = run.ChannelsChecked,
            FoundVideos = run.NewVideosFound,
            ProcessedVideos = run.VideosIngested,
            FailedVideos = run.VideosFailed,
            Repositories = run.RepositoriesFound,
            Websites = perItemWebsiteCounts.Values.Sum()
        },
        LiveRollup = new IngestionRunOutcomeViewModel
        {
            Heading = "Live rollup",
            Caption = "Derived from current ingestion item state.",
            Channels = items.Count(item => item.ItemType.Equals("channel", StringComparison.OrdinalIgnoreCase)),
            FoundVideos = items.Count(item => item.ItemType.Equals("video", StringComparison.OrdinalIgnoreCase)),
            ProcessedVideos = items.Count(item => IsProcessedStatus(item.Status)),
            FailedVideos = items.Count(item => item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)),
            Repositories = items.Count(item => item.ItemType.StartsWith("repository", StringComparison.OrdinalIgnoreCase)),
            Websites = items.Count(item => item.ItemType.StartsWith("website", StringComparison.OrdinalIgnoreCase) || item.ItemType.Equals("resource", StringComparison.OrdinalIgnoreCase))
        },
        Stages = stageRollups,
        Items = mappedItems,
        Deferments = deferments,
        Links =
        [
            new IngestionRunLinkViewModel
            {
                Label = "Hangfire",
                Url = "/admin/jobs"
            },
            new IngestionRunLinkViewModel
            {
                Label = "Notifications",
                Url = $"/api/internal/ingestion-runs/{ingestionRunId}/notifications"
            }
        ]
    };

    return Results.Ok(viewModel);
});
app.MapGet("/api/internal/ingestion-runs/{ingestionRunId:guid}/notifications", async (Guid ingestionRunId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var notifications = await context.Notifications
        .Where(notification => notification.IngestionRunId == ingestionRunId)
        .OrderByDescending(notification => notification.CreatedAt)
        .Select(notification => new
        {
            notification.Id,
            notification.OperationId,
            notification.Provider,
            notification.Target,
            notification.Status,
            notification.AttemptCount,
            notification.NextRetryAt,
            notification.ProviderMessageId,
            notification.ErrorSummary,
            notification.SentAt,
            notification.CreatedAt,
            notification.UpdatedAt,
            Retryable = notification.Status == "pending" || notification.Status == "failed"
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(notifications);
});
app.MapGet("/api/videos/{videoId:guid}/transcript", async (Guid videoId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var transcript = await context.VideoTranscripts
        .AsNoTracking()
        .Include(candidate => candidate.Cues)
        .SingleOrDefaultAsync(candidate => candidate.VideoId == videoId && candidate.IsActive, cancellationToken);

    if (transcript is null)
    {
        return Results.NotFound();
    }

    var response = new VideoTranscriptResponse(
        transcript.Id,
        transcript.VideoId,
        transcript.SourceType,
        transcript.LanguageCode,
        transcript.Cues
            .OrderBy(cue => cue.Sequence)
            .Select(cue => new TranscriptCueResponse(
                cue.Id,
                cue.Sequence,
                cue.StartSeconds,
                cue.EndSeconds,
                cue.TextOriginal,
                cue.TextOverride,
                cue.TextOverride ?? cue.TextOriginal))
            .ToArray());

    return Results.Ok(response);
});
app.MapPut("/api/videos/{videoId:guid}/overrides", async (Guid videoId, UpdateVideoOverrideRequest request, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var video = await context.Videos.SingleOrDefaultAsync(v => v.Id == videoId, cancellationToken);
    if (video is null)
    {
        return Results.NotFound();
    }

    var historyEntries = new List<FieldOverrideHistory>();
    if (request.Title is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "title", PreviousValue = video.TitleOverride, NewValue = newValue });
        video.TitleOverride = newValue;
    }
    if (request.Author is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "author", PreviousValue = video.AuthorOverride, NewValue = newValue });
        video.AuthorOverride = newValue;
    }
    if (request.Description is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "video", EntityId = videoId, FieldName = "description", PreviousValue = video.DescriptionOverride, NewValue = newValue });
        video.DescriptionOverride = newValue;
    }

    context.FieldOverrideHistories.AddRange(historyEntries);
    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPut("/api/segments/{segmentId:guid}/overrides", async (Guid segmentId, UpdateSegmentOverrideRequest request, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var segment = await context.Segments.SingleOrDefaultAsync(s => s.Id == segmentId, cancellationToken);
    if (segment is null)
    {
        return Results.NotFound();
    }

    var historyEntries = new List<FieldOverrideHistory>();
    if (request.Title is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "segment", EntityId = segmentId, FieldName = "title", PreviousValue = segment.TitleOverride, NewValue = newValue });
        segment.TitleOverride = newValue;
    }
    if (request.Summary is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "segment", EntityId = segmentId, FieldName = "summary", PreviousValue = segment.SummaryOverride, NewValue = newValue });
        segment.SummaryOverride = newValue;
    }

    context.FieldOverrideHistories.AddRange(historyEntries);
    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPut("/api/transcript-cues/{cueId:guid}/overrides", async (Guid cueId, UpdateTranscriptCueOverrideRequest request, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var cue = await context.TranscriptCues.SingleOrDefaultAsync(candidate => candidate.Id == cueId, cancellationToken);
    if (cue is null)
    {
        return Results.NotFound();
    }

    var newValue = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();
    context.FieldOverrideHistories.Add(new FieldOverrideHistory { EntityType = "transcript_cue", EntityId = cueId, FieldName = "text", PreviousValue = cue.TextOverride, NewValue = newValue });
    cue.TextOverride = newValue;
    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPut("/api/external-resources/{resourceId:guid}/overrides", async (Guid resourceId, UpdateExternalResourceOverrideRequest request, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var resource = await context.ExternalResources.SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
    if (resource is null)
    {
        return Results.NotFound();
    }

    var historyEntries = new List<FieldOverrideHistory>();
    if (request.Title is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "title", PreviousValue = resource.TitleOverride, NewValue = newValue });
        resource.TitleOverride = newValue;
    }
    if (request.Description is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "description", PreviousValue = resource.DescriptionOverride, NewValue = newValue });
        resource.DescriptionOverride = newValue;
    }
    if (request.Classification is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Classification) ? null : request.Classification.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "external_resource", EntityId = resourceId, FieldName = "classification", PreviousValue = resource.ClassificationOverride, NewValue = newValue });
        resource.ClassificationOverride = newValue;
    }

    context.FieldOverrideHistories.AddRange(historyEntries);
    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPut("/api/repositories/{repositoryId:guid}/overrides", async (Guid repositoryId, UpdateRepositoryOverrideRequest request, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var repo = await context.Repositories.SingleOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);
    if (repo is null)
    {
        return Results.NotFound();
    }

    var historyEntries = new List<FieldOverrideHistory>();
    if (request.Description is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "description", PreviousValue = repo.DescriptionOverride, NewValue = newValue });
        repo.DescriptionOverride = newValue;
    }
    if (request.PrimaryLanguage is not null)
    {
        var newValue = string.IsNullOrWhiteSpace(request.PrimaryLanguage) ? null : request.PrimaryLanguage.Trim();
        historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "primary_language", PreviousValue = repo.PrimaryLanguage, NewValue = newValue });
        repo.PrimaryLanguage = newValue;
    }
    if (request.Topics is not null)
    {
        var newTopics = request.Topics.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
        var previousTopics = repo.Topics is { Length: > 0 } ? string.Join(",", repo.Topics) : null;
        var newTopicsStr = newTopics.Length > 0 ? string.Join(",", newTopics) : null;
        historyEntries.Add(new FieldOverrideHistory { EntityType = "repository", EntityId = repositoryId, FieldName = "topics", PreviousValue = previousTopics, NewValue = newTopicsStr });
        repo.Topics = newTopics.Length > 0 ? newTopics : null;
    }

    context.FieldOverrideHistories.AddRange(historyEntries);
    await context.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/overrides/history", async (string? entityType, Guid? entityId, string? fieldName, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
{
    var query = context.FieldOverrideHistories.AsQueryable();
    if (!string.IsNullOrWhiteSpace(entityType))
    {
        query = query.Where(h => h.EntityType == entityType);
    }
    if (entityId.HasValue)
    {
        query = query.Where(h => h.EntityId == entityId.Value);
    }
    if (!string.IsNullOrWhiteSpace(fieldName))
    {
        query = query.Where(h => h.FieldName == fieldName);
    }

    var entries = await query
        .OrderByDescending(h => h.ChangedAt)
        .Select(h => new FieldOverrideHistoryResponse(h.Id, h.EntityType, h.EntityId, h.FieldName, h.PreviousValue, h.NewValue, h.ChangedAt))
        .ToListAsync(cancellationToken);

    return Results.Ok(entries);
});


app.MapPost("/api/auth/login", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
{
    try
    {
        var requestBody = await JsonSerializer.DeserializeAsync<LoginRequest>(context.Request.Body, jsonOptions, cancellationToken);
        if (requestBody is null)
        {
            return Results.BadRequest(new { error = "The request body is required." });
        }

        var result = await authService.AuthenticateAsync(
            connectionString,
            requestBody.Username ?? string.Empty,
            requestBody.Password ?? string.Empty,
            context.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!result.Success)
        {
            return Results.Json(new { error = result.ErrorMessage }, statusCode: result.StatusCode);
        }

        context.Response.Cookies.Append("auth-session", result.SessionToken!, CreateSessionCookieOptions());
        context.Response.Cookies.Append("csrf-token", result.CsrfToken!, CreateCsrfCookieOptions());

        return Results.Ok(new { username = result.User!.Username, mustChangePassword = result.User.MustChangePassword });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Login request failed.");
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Login failed", detail: ex.Message);
    }
});
app.MapPost("/api/auth/logout", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
{
    var sessionToken = context.Request.Cookies["auth-session"];
    if (!string.IsNullOrWhiteSpace(sessionToken))
    {
        await authService.LogoutAsync(connectionString, sessionToken, cancellationToken);
    }

    context.Response.Cookies.Delete("auth-session", new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });
    context.Response.Cookies.Delete("csrf-token", new CookieOptions { Path = "/", HttpOnly = false, Secure = true, SameSite = SameSiteMode.Lax });

    return Results.Ok(new { success = true });
});
app.MapGet("/api/auth/me", (HttpContext context) =>
{
    var authenticatedUser = context.Items["AuthenticatedUser"] as AuthenticatedUser;
    return authenticatedUser is null
        ? Results.Unauthorized()
        : Results.Ok(new { username = authenticatedUser.Username, mustChangePassword = authenticatedUser.MustChangePassword });
});
app.MapGet("/api/auth/csrf", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
{
    var csrfToken = string.Empty;
    var authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request, cancellationToken);
    if (authenticationResult.IsAuthenticated && !string.IsNullOrWhiteSpace(authenticationResult.CsrfToken))
    {
        csrfToken = authenticationResult.CsrfToken!;
    }

    if (string.IsNullOrWhiteSpace(csrfToken))
    {
        csrfToken = authService.CreateCsrfToken();
    }

    context.Response.Cookies.Append("csrf-token", csrfToken, CreateCsrfCookieOptions());
    return Results.Ok(new { token = csrfToken });
});
app.MapPost("/api/auth/change-password", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
{
    var authenticatedUser = context.Items["AuthenticatedUser"] as AuthenticatedUser;
    if (authenticatedUser is null)
    {
        return Results.Unauthorized();
    }

    var authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request, cancellationToken);
    if (!authenticationResult.IsAuthenticated || !HasValidCsrfToken(context.Request, authenticationResult.CsrfToken))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var requestBody = await JsonSerializer.DeserializeAsync<ChangePasswordRequest>(context.Request.Body, jsonOptions, cancellationToken);
    if (requestBody is null || string.IsNullOrWhiteSpace(requestBody.CurrentPassword) || string.IsNullOrWhiteSpace(requestBody.NewPassword))
    {
        return Results.BadRequest(new { error = "Both current and new passwords are required." });
    }

    var changed = await authService.ChangePasswordAsync(connectionString, authenticatedUser.Id, requestBody.CurrentPassword, requestBody.NewPassword, cancellationToken);
    return changed
        ? Results.Ok(new { status = "updated" })
        : Results.BadRequest(new { error = "The current password is invalid or the new password is too weak." });
});

app.MapGet("/api/onboarding/status", async (HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(await readinessStateService.GetStatusAsync(connectionString, cancellationToken))
        : Results.Unauthorized());
app.MapPost("/api/onboarding/steps/{stepKey}/verify", async (string stepKey, HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
{
    if (!IsAuthenticated(context.Request))
    {
        return Results.Unauthorized();
    }

    OnboardingStepVerificationRequest? request = null;
    if (context.Request.ContentLength is > 0)
    {
        try
        {
            request = await JsonSerializer.DeserializeAsync<OnboardingStepVerificationRequest>(context.Request.Body, jsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "The request body is invalid." });
        }
    }

    var result = await readinessStateService.VerifyStepAsync(connectionString, stepKey, request, cancellationToken);
    return Results.Ok(result);
});
app.MapPost("/api/onboarding/complete-core-setup", async (HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(await readinessStateService.CompleteCoreSetupAsync(connectionString, cancellationToken))
        : Results.Unauthorized());

app.MapGet("/api/settings", (HttpContext context) =>
    IsAuthenticated(context.Request)
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
    if (!IsAuthenticated(context.Request))
    {
        return Results.Unauthorized();
    }

    if (!HasCsrfToken(context.Request))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        var body = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(context.Request.Body, cancellationToken: cancellationToken);
        if (body is not null && body.TryGetValue("observability.enabled", out var enabledValue) && enabledValue.ValueKind == JsonValueKind.True)
        {
            await PersistObservabilitySettingAsync(connectionString, app.Logger, true, cancellationToken);
            observabilityRuntime.Enabled = true;
            await EnsureObservabilityReadinessAsync(app.Logger, observabilityRuntime);
        }
        else if (body is not null && body.TryGetValue("observability.enabled", out enabledValue) && enabledValue.ValueKind == JsonValueKind.False)
        {
            await PersistObservabilitySettingAsync(connectionString, app.Logger, false, cancellationToken);
            observabilityRuntime.Enabled = false;
            observabilityRuntime.Ready = false;
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to process observability settings update");
    }

    return Results.Ok(new { status = "updated" });
});
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
        observabilityRuntime.PrometheusUrl,
        observabilityRuntime.LokiUrl,
        observabilityRuntime.TempoUrl),
    services = new
    {
        hangfire = observabilityRuntime.HangfireUrl,
        grafana = observabilityRuntime.GrafanaUrl,
        prometheus = observabilityRuntime.PrometheusUrl,
        loki = observabilityRuntime.LokiUrl,
        tempo = observabilityRuntime.TempoUrl,
        otelCollector = observabilityRuntime.OtelCollectorUrl
    }
}));
app.MapPost("/api/observability/toggle/{enabled:bool}", async (bool enabled, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!IsAuthenticated(context.Request))
    {
        return Results.Unauthorized();
    }

    await PersistObservabilitySettingAsync(connectionString, app.Logger, enabled, cancellationToken);
    observabilityRuntime.Enabled = enabled;
    if (enabled)
    {
        await EnsureObservabilityReadinessAsync(app.Logger, observabilityRuntime);
    }
    else
    {
        observabilityRuntime.Ready = false;
    }

    return Results.Ok(new { enabled, mode = enabled ? "enabled" : "disabled" });
});

app.MapMethods("/grafana/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "grafana", observabilityRuntime.GrafanaUrl, cancellationToken));
app.MapMethods("/prometheus/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "prometheus", observabilityRuntime.PrometheusUrl, cancellationToken));
app.MapMethods("/loki/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "loki", observabilityRuntime.LokiUrl, cancellationToken));
app.MapMethods("/tempo/{**catchAll}", ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD"], async (HttpContext context, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
    await ProxyObservabilityRequestAsync(context, httpClientFactory, observabilityRuntime, "tempo", observabilityRuntime.TempoUrl, cancellationToken));

app.MapGet("/api/config/runtime", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new
        {
            environment = app.Environment.EnvironmentName,
            deployMode = "local",
            features = new[] { "settings", "models" }
        })
        : Results.Unauthorized());
app.MapGet("/api/config/schema", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new
        {
            version = "1.0",
            properties = new[]
            {
                new { key = "environment", type = "string" },
                new { key = "deployMode", type = "string" }
            }
        })
        : Results.Unauthorized());
app.MapPost("/api/config/validate", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new { valid = true, warnings = Array.Empty<string>() })
        : Results.Unauthorized());

app.MapGet("/api/models/options", (HttpContext context, ModelDiscoveryService modelDiscoveryService) =>
    IsAuthenticated(context.Request)
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
    if (!IsAuthenticated(context.Request))
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
    if (!IsAuthenticated(context.Request))
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

app.MapGet("/api/notes", async (string? targetType, Guid? targetId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
{
    var query = dbContext.Notes.AsNoTracking().Where(n => n.DeletedAt == null);
    if (!string.IsNullOrWhiteSpace(targetType))
    {
        query = query.Where(n => n.TargetType == targetType);
    }
    if (targetId.HasValue)
    {
        query = query.Where(n => n.TargetId == targetId.Value);
    }

    var notes = await query
        .OrderByDescending(n => n.UpdatedAt)
        .Select(n => new NoteResponse(n.Id, n.TargetType, n.TargetId, n.Title, n.Markdown, n.EmbeddingStatus, n.CreatedAt, n.UpdatedAt))
        .ToListAsync(cancellationToken);

    return Results.Ok(new { items = notes });
});

app.MapPost("/api/notes", async (CreateNoteRequest request, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.TargetType))
    {
        return Results.BadRequest(new { error = "targetType is required." });
    }

    if (request.TargetId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "targetId is required." });
    }

    if (string.IsNullOrWhiteSpace(request.Markdown))
    {
        return Results.BadRequest(new { error = "markdown is required." });
    }

    var liveNoteExists = await dbContext.Notes
        .AnyAsync(n => n.TargetType == request.TargetType && n.TargetId == request.TargetId && n.DeletedAt == null, cancellationToken);

    if (liveNoteExists)
    {
        return Results.Conflict(new { error = "A live note already exists for this target. Use PUT to update it." });
    }

    var note = new StreamingDigest.Domain.Note
    {
        TargetType = request.TargetType.Trim(),
        TargetId = request.TargetId,
        Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
        Markdown = request.Markdown.Trim(),
        EmbeddingStatus = "stale"
    };

    dbContext.Notes.Add(note);
    await dbContext.SaveChangesAsync(cancellationToken);

    var response = new NoteResponse(note.Id, note.TargetType, note.TargetId, note.Title, note.Markdown, note.EmbeddingStatus, note.CreatedAt, note.UpdatedAt);
    return Results.Created($"/api/notes/{note.Id}", response);
});

app.MapGet("/api/notes/{noteId:guid}", async (Guid noteId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
{
    var note = await dbContext.Notes
        .AsNoTracking()
        .Where(n => n.Id == noteId && n.DeletedAt == null)
        .Select(n => new NoteResponse(n.Id, n.TargetType, n.TargetId, n.Title, n.Markdown, n.EmbeddingStatus, n.CreatedAt, n.UpdatedAt))
        .SingleOrDefaultAsync(cancellationToken);

    return note is null ? Results.NotFound() : Results.Ok(note);
});

app.MapPut("/api/notes/{noteId:guid}", async (Guid noteId, UpdateNoteRequest request, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
{
    var note = await dbContext.Notes.SingleOrDefaultAsync(n => n.Id == noteId && n.DeletedAt == null, cancellationToken);
    if (note is null)
    {
        return Results.NotFound();
    }

    if (request.Title is not null)
    {
        note.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
    }

    if (request.Markdown is not null)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return Results.BadRequest(new { error = "markdown cannot be blank." });
        }

        note.Markdown = request.Markdown.Trim();
    }

    note.EmbeddingStatus = "stale";
    await dbContext.SaveChangesAsync(cancellationToken);

    var response = new NoteResponse(note.Id, note.TargetType, note.TargetId, note.Title, note.Markdown, note.EmbeddingStatus, note.CreatedAt, note.UpdatedAt);
    return Results.Ok(new { status = "updated", entityType = "note", entityId = note.Id, resource = response });
});

app.MapDelete("/api/notes/{noteId:guid}", async (Guid noteId, StreamingDigestDbContext dbContext, CancellationToken cancellationToken) =>
{
    var note = await dbContext.Notes.SingleOrDefaultAsync(n => n.Id == noteId && n.DeletedAt == null, cancellationToken);
    if (note is null)
    {
        return Results.NotFound();
    }

    note.DeletedAt = DateTimeOffset.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { status = "deleted", entityType = "note", entityId = noteId });
});

app.MapGet("/{*path}", async context =>
{
    if (!ShouldServeSpaFallback(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var webRootPath = app.Environment.WebRootPath;
    if (string.IsNullOrWhiteSpace(webRootPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(webRootPath, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();

static async Task<DatabaseStatus> EnsureDatabaseConnectivityAsync(ILogger logger, string connectionString)
{
    using var activity = CorrelationContext.BeginOperation("database.connectivity", ActivityKind.Client, new Dictionary<string, object?>
    {
        ["db.system"] = "postgresql",
        ["db.operation"] = "connect"
    });

    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);

    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT current_database(), current_setting('server_version')", connection);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("No result returned from PostgreSQL health query");
        }

        var databaseName = reader.GetString(0);
        var serverVersion = reader.GetString(1);

        logger.LogInformation("API database connectivity confirmed for {Database} on {Host}:{Port}", databaseName, connectionStringBuilder.Host, connectionStringBuilder.Port);

        return new DatabaseStatus(true, databaseName, serverVersion);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "API could not connect to PostgreSQL; the API will continue in degraded mode");
        return new DatabaseStatus(false, connectionStringBuilder.Database ?? "postgres", "unavailable");
    }
}

static JobStorage CreateHangfireStorage(ILogger logger, string connectionString, bool databaseConnected)
{
    if (!databaseConnected)
    {
        return new MemoryStorage();
    }

    try
    {
        return new PostgreSqlStorage(connectionString);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Hangfire PostgreSQL storage initialization failed; the dashboard will use in-memory storage for this startup.");
        return new MemoryStorage();
    }
}

static void ConfigureOtlpExporter(OtlpExporterOptions options)
{
    var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
        ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")
        ?? "http://localhost:18889";

    options.Protocol = OtlpExportProtocol.Grpc;
    options.Endpoint = new Uri(endpoint);
}

static bool ResolveDefaultObservabilityEnabled(IConfiguration configuration, IHostEnvironment environment, bool fallbackEnabled)
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

static bool IsLocalhostUrl(IConfiguration configuration)
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

static async Task<ObservabilityRuntimeState> LoadObservabilityRuntimeAsync(ILogger logger, IConfiguration configuration, IHostEnvironment environment, string connectionString, bool databaseConnected, bool defaultEnabled, int defaultRetentionDays)
{
    var resolvedEnabled = ResolveDefaultObservabilityEnabled(configuration, environment, defaultEnabled);
    var resolvedRetentionDays = defaultRetentionDays;
    var grafanaUrl = configuration["observability:services:grafana:url"] ?? "http://grafana:3000";
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
        PrometheusUrl = prometheusUrl,
        LokiUrl = lokiUrl,
        TempoUrl = tempoUrl,
        HangfireUrl = hangfireUrl,
        OtelCollectorUrl = otelCollectorUrl
    };
}

static async Task EnsureObservabilityReadinessAsync(ILogger logger, ObservabilityRuntimeState observabilityRuntime)
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

static async Task PersistObservabilitySettingAsync(string connectionString, ILogger logger, bool enabled, CancellationToken cancellationToken)
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

static async Task<bool> ReadBoolSettingAsync(NpgsqlConnection connection, string key, bool fallback)
{
    return await AppSettingReader.ReadBoolAsync(connection, key, fallback);
}

static async Task<int> ReadIntSettingAsync(NpgsqlConnection connection, string key, int fallback)
{
    return await AppSettingReader.ReadIntAsync(connection, key, fallback);
}

static async Task<string> ReadStringSettingAsync(NpgsqlConnection connection, string key, string fallback)
{
    return await AppSettingReader.ReadStringAsync(connection, key, fallback);
}

static async Task<IResult> ProxyObservabilityRequestAsync(HttpContext context, IHttpClientFactory httpClientFactory, ObservabilityRuntimeState observabilityRuntime, string serviceName, string upstreamBaseUrl, CancellationToken cancellationToken)
{
    if (!observabilityRuntime.Enabled)
    {
        return Results.Content(BuildDisabledPlaceholder(serviceName), "text/html", Encoding.UTF8);
    }

    try
    {
        using var client = httpClientFactory.CreateClient();
        var targetUri = BuildProxyUri(context.Request.Path, upstreamBaseUrl, context.Request.QueryString.Value);
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

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
        return Results.Bytes(responseBytes, contentType);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
}

static bool ShouldForwardRequestBody(HttpRequest request)
{
    return request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");
}

static bool RequiresAuthentication(HttpRequest request)
{
    var path = request.Path.Value ?? string.Empty;
    if (path.Equals("/api/observability", StringComparison.Ordinal))
    {
        return false;
    }

    if (IsHangfireDashboardPath(path))
    {
        return true;
    }

    return path.StartsWith("/api/internal", StringComparison.Ordinal)
        || path.StartsWith("/api/onboarding", StringComparison.Ordinal)
        || path.StartsWith("/api/settings", StringComparison.Ordinal)
        || path.StartsWith("/api/observability/toggle", StringComparison.Ordinal)
        || path.StartsWith("/api/config", StringComparison.Ordinal)
        || path.StartsWith("/api/auth/me", StringComparison.Ordinal)
        || path.StartsWith("/api/auth/change-password", StringComparison.Ordinal);
}

static bool RequiresCsrfProtection(HttpRequest request)
{
    if (IsHangfireDashboardPath(request.Path.Value ?? string.Empty))
    {
        return false;
    }

    return request.Method is not "GET" and not "HEAD" and not "OPTIONS" and not "TRACE";
}

static bool IsHangfireDashboardPath(string path)
{
    return path.Equals("/admin/jobs", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/admin/jobs/", StringComparison.OrdinalIgnoreCase);
}

static bool HasValidCsrfToken(HttpRequest request, string? expectedToken)
{
    if (string.IsNullOrWhiteSpace(expectedToken))
    {
        return false;
    }

    var requestToken = request.Headers["X-CSRF-Token"].ToString();
    if (!string.IsNullOrWhiteSpace(requestToken))
    {
        return string.Equals(requestToken, expectedToken, StringComparison.Ordinal);
    }

    return request.Cookies.TryGetValue("csrf-token", out var cookieToken) && string.Equals(cookieToken, expectedToken, StringComparison.Ordinal);
}

static bool IsAuthenticated(HttpRequest request)
{
    return request.Cookies.ContainsKey("auth-session");
}

static bool HasCsrfToken(HttpRequest request)
{
    return request.Headers.ContainsKey("X-CSRF-Token") || request.Cookies.ContainsKey("csrf-token");
}

static bool ShouldAllowPasswordChangeFlow(HttpRequest request)
{
    var path = request.Path.Value ?? string.Empty;
    return path.StartsWith("/api/auth/change-password", StringComparison.Ordinal)
        || path.StartsWith("/api/auth/me", StringComparison.Ordinal)
        || path.StartsWith("/api/auth/csrf", StringComparison.Ordinal)
        || path.StartsWith("/api/auth/logout", StringComparison.Ordinal);
}

static CookieOptions CreateSessionCookieOptions()
{
    return new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(8)
    };
}

static CookieOptions CreateCsrfCookieOptions()
{
    return new CookieOptions
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromHours(8)
    };
}

static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
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

static Uri BuildProxyUri(PathString requestPath, string upstreamBaseUrl, string? query)
{
    var baseUri = new Uri(upstreamBaseUrl.EndsWith('/') ? upstreamBaseUrl : upstreamBaseUrl + "/");
    var relativePath = requestPath.Value?.Replace("/grafana", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("/prometheus", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("/loki", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("/tempo", string.Empty, StringComparison.OrdinalIgnoreCase)
        ?? string.Empty;

    var sanitizedPath = string.IsNullOrWhiteSpace(relativePath) ? "/" : relativePath;
    var builder = new UriBuilder(baseUri)
    {
        Path = sanitizedPath,
        Query = query?.TrimStart('?') ?? string.Empty
    };

    return builder.Uri;
}

static string BuildDisabledPlaceholder(string serviceName)
{
    return $"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
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

static bool ShouldServeSpaFallback(PathString path)
{
    if (path.StartsWithSegments("/api") ||
        path.StartsWithSegments("/admin") ||
        path.StartsWithSegments("/internal") ||
        path.StartsWithSegments("/grafana") ||
        path.StartsWithSegments("/prometheus") ||
        path.StartsWithSegments("/loki") ||
        path.StartsWithSegments("/tempo"))
    {
        return false;
    }

    return true;
}

internal sealed record CreateChannelRequest(string? SourceUrl, int? DefaultMaxAgeDays, int? DefaultBackfillMaxVideos);
internal sealed record UpdateChannelRequest(string? NameOverride, string? DescriptionOverride, bool? IsPaused, int? DefaultMaxAgeDays, int? DefaultBackfillMaxVideos);
internal sealed record UpdateTranscriptCueOverrideRequest(string? Text);
internal sealed record ChannelListItemResponse(Guid Id, string YoutubeChannelId, string Name, string ProfileUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus);
internal sealed record ChannelDetailResponse(Guid Id, string YoutubeChannelId, ChannelValueResponse Name, ChannelValueResponse Description, string ProfileUrl, string SourceUrl, bool IsPaused, bool IsDegraded, int ConsecutiveFailures, DateTimeOffset? LastIngestedAt, string? LastIngestionStatus, ChannelIngestionDefaultsResponse IngestionDefaults);
internal sealed record ChannelValueResponse(string? Original, string? Override, string? Effective);
internal sealed record ChannelIngestionDefaultsResponse(int? MaxAgeDays, int? BackfillMaxVideos);
internal sealed record VideoTranscriptResponse(Guid Id, Guid VideoId, string SourceType, string? LanguageCode, IReadOnlyList<TranscriptCueResponse> Cues);
internal sealed record TranscriptCueResponse(Guid Id, int Sequence, decimal StartSeconds, decimal? EndSeconds, string TextOriginal, string? TextOverride, string Text);
internal sealed record UpdateVideoOverrideRequest(string? Title, string? Author, string? Description);
internal sealed record UpdateSegmentOverrideRequest(string? Title, string? Summary);
internal sealed record UpdateExternalResourceOverrideRequest(string? Title, string? Description, string? Classification);
internal sealed record UpdateRepositoryOverrideRequest(string? Description, string? PrimaryLanguage, string[]? Topics);
internal sealed record FieldOverrideHistoryResponse(Guid Id, string EntityType, Guid EntityId, string FieldName, string? PreviousValue, string? NewValue, DateTimeOffset ChangedAt);
internal sealed record CreateNoteRequest(string TargetType, Guid TargetId, string? Title, string Markdown);
internal sealed record UpdateNoteRequest(string? Title, string? Markdown);
internal sealed record NoteResponse(Guid Id, string TargetType, Guid TargetId, string? Title, string Markdown, string EmbeddingStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

sealed class PassThroughDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

sealed record ModelDiscoveryRequest(string? ModelKind, string? ModelId);

public sealed record LoginRequest(string Username, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);

public sealed class ObservabilityRuntimeState
{
    public bool Enabled { get; set; }
    public bool Ready { get; set; }
    public int RetentionDays { get; set; }
    public bool RetentionWarning { get; set; }
    public string GrafanaUrl { get; set; } = string.Empty;
    public string PrometheusUrl { get; set; } = string.Empty;
    public string LokiUrl { get; set; } = string.Empty;
    public string TempoUrl { get; set; } = string.Empty;
    public string HangfireUrl { get; set; } = string.Empty;
    public string OtelCollectorUrl { get; set; } = string.Empty;
}

public partial class Program;
