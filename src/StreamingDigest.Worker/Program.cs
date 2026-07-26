using System.Diagnostics;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Observability;
using StreamingDigest.Application.Screenshots;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.MatrixNotifier;
using StreamingDigest.Worker;
using StreamingDigest.Worker.Scraping;

var builder = Host.CreateApplicationBuilder(args);

var applicationConfiguration = ApplicationConfigurationLoader.LoadFromDirectory(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(applicationConfiguration);

var workerConcurrencySettings = new WorkerConcurrencySettings();
builder.Services.AddSingleton(workerConcurrencySettings);
builder.Services.AddSingleton<WorkerOperationConcurrencyController>();

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

var connectionString = builder.Configuration.GetConnectionString("streamingdigest")
    ?? builder.Configuration.GetConnectionString("postgres")
    ?? applicationConfiguration.ConnectionStrings.StreamingDigest;

builder.Services.AddHangfire(config => config.UsePostgreSqlStorage(connectionString));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
});
builder.Services.AddDbContext<StreamingDigestDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IScreenshotGenerationService, ScreenshotGenerationService>();
builder.Services.AddHttpClient<ILinkClassificationService, LinkClassificationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    var llmBaseUrl = builder.Configuration["llm:baseUrl"]
        ?? Environment.GetEnvironmentVariable("STREAMINGDIGEST_LLM_BASE_URL")
        ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
        ?? Environment.GetEnvironmentVariable("OLLAMA_HOST");
    if (!string.IsNullOrWhiteSpace(llmBaseUrl))
    {
        client.BaseAddress = new Uri(llmBaseUrl);
    }
});
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
builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
builder.Services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
builder.Services.AddScoped<IScrapeFailureRecorder, ScrapeFailureRecorder>();
builder.Services.AddHttpClient<ScraperClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Scraper:BaseUrl"] ?? "http://localhost:3000");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var host = builder.Build();
var environmentName = builder.Environment.EnvironmentName;

using var startupScope = CorrelationContext.BeginLoggingScope(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"), new Dictionary<string, object?>
{
    ["startup"] = "worker",
    ["environment"] = environmentName
});

var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var databaseStatus = await EnsureDatabaseConnectivityAsync(startupLogger, connectionString);
var defaultObservabilityEnabled = ResolveDefaultObservabilityEnabled(builder.Configuration, builder.Environment, false);
var defaultRetentionDays = StorageRetentionPolicy.ComputeObservabilityRetentionDays(StorageRetentionPolicy.GetMaximumReadyDriveFreeSpaceBytes());

if (databaseStatus.Connected)
{
    var migrationRunner = new PostgresMigrationRunner(connectionString);
    await migrationRunner.ApplyAsync();

    var seeder = new AppSettingsSeeder(startupLogger);
    await seeder.SeedDefaultsAsync(connectionString, defaultObservabilityEnabled, defaultRetentionDays);
}

await WorkerConcurrencySettingsLoader.LoadAsync(connectionString, databaseStatus.Connected, startupLogger, workerConcurrencySettings);
WorkerConcurrencySettingsLoader.LogResolvedSettings(startupLogger, workerConcurrencySettings);

var scraperClient = host.Services.GetRequiredService<ScraperClient>();
var scraperHealthy = await scraperClient.IsHealthyAsync();
startupLogger.LogInformation("Scraper health check result: {Status}", scraperHealthy ? "healthy" : "unreachable");

await host.RunAsync();

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

        logger.LogInformation("Worker database connectivity confirmed for {Database} ({ServerVersion}) on {Host}:{Port}", databaseName, serverVersion, connectionStringBuilder.Host, connectionStringBuilder.Port);
        return new DatabaseStatus(true, databaseName, serverVersion);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Worker could not connect to PostgreSQL; the worker will continue in degraded mode");
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

    return environment.IsDevelopment() ? true : fallbackEnabled;
}

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);
