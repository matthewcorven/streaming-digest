using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StreamingDigest.Api.Observability;
using StreamingDigest.Application.Observability;
using Npgsql;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.MatrixNotifier;

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
        tracing.AddOtlpExporter(options => ConfigureOtlpExporter(options));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CorrelationContext.ActivitySourceName);
        metrics.AddOtlpExporter(options => ConfigureOtlpExporter(options));
    });

GlobalJobFilters.Filters.Add(new HangfireObservabilityFilter());

builder.Services.AddOpenApi();
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

builder.Services.AddDbContext<StreamingDigestDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();

var app = builder.Build();

using var startupScope = CorrelationContext.BeginLoggingScope(app.Logger, new Dictionary<string, object?>
{
    ["startup"] = "api",
    ["environment"] = app.Environment.EnvironmentName
});

var databaseStatus = await EnsureDatabaseConnectivityAsync(app.Logger, connectionString);

if (databaseStatus.Connected)
{
    var seeder = new AppSettingsSeeder(app.Logger);
    await seeder.SeedDefaultsAsync(connectionString);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

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
app.MapFallbackToFile("index.html");

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

static void ConfigureOtlpExporter(OtlpExporterOptions options)
{
    var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
        ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")
        ?? "http://localhost:18889";

    options.Protocol = OtlpExportProtocol.Grpc;
    options.Endpoint = new Uri(endpoint);
}

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);

public partial class Program;
