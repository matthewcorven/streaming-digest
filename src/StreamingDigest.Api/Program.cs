using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StreamingDigest.Api;
using StreamingDigest.Api.Endpoints;
using StreamingDigest.Api.Observability;
using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Observability;
using StreamingDigest.Infrastructure.Persistence;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
const string webClientCorsPolicy = "web-client";

var builder = WebApplication.CreateBuilder(args);
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToHashSet(StringComparer.OrdinalIgnoreCase)
    ?? [];

var applicationConfiguration = ApplicationConfigurationLoader.LoadFromDirectory(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(applicationConfiguration);
builder.Services.AddCors(options =>
{
    options.AddPolicy(webClientCorsPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin => allowedCorsOrigins.Contains(origin))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

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
    logging.AddOtlpExporter(options => ApiStartupRuntime.ConfigureOtlpExporter(options));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddSource(CorrelationContext.ActivitySourceName);
        tracing.AddSource("Experimental.Microsoft.Extensions.AI");
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddOtlpExporter(options => ApiStartupRuntime.ConfigureOtlpExporter(options));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CorrelationContext.ActivitySourceName);
        metrics.AddMeter("Experimental.Microsoft.Extensions.AI");
        metrics.AddOtlpExporter(options => ApiStartupRuntime.ConfigureOtlpExporter(options));
    });

GlobalJobFilters.Filters.Add(new HangfireObservabilityFilter());

var startupState = await ApiStartupRuntime.InitializeInfrastructureAsync(builder, applicationConfiguration);
var connectionString = startupState.ConnectionString;
var databaseStatus = startupState.DatabaseStatus;
builder.Services.AddStreamingDigestApiServices(
    builder.Configuration,
    builder.Environment,
    applicationConfiguration,
    connectionString,
    startupState.HangfireStorage);

var app = builder.Build();
var authService = app.Services.GetRequiredService<AppAuthService>();
var useSecureCookies = !app.Environment.IsDevelopment();

using var startupScope = CorrelationContext.BeginLoggingScope(app.Logger, new Dictionary<string, object?>
{
    ["startup"] = "api",
    ["environment"] = app.Environment.EnvironmentName
});

var defaultObservabilityEnabled = ObservabilityEndpoints.ResolveDefaultObservabilityEnabled(builder.Configuration, builder.Environment, false);
var defaultRetentionDays = StorageRetentionPolicy.ComputeObservabilityRetentionDays(StorageRetentionPolicy.GetMaximumReadyDriveFreeSpaceBytes());
var observabilityRuntime = await ObservabilityEndpoints.LoadObservabilityRuntimeAsync(
    app.Logger,
    builder.Configuration,
    builder.Environment,
    connectionString,
    databaseStatus.Connected,
    defaultObservabilityEnabled,
    defaultRetentionDays);
await ObservabilityEndpoints.EnsureObservabilityReadinessAsync(app.Logger, observabilityRuntime);

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

    var modelRuntimeStateSchemaGuard = app.Services.GetRequiredService<IModelRuntimeStateSchemaGuard>();
    await modelRuntimeStateSchemaGuard.EnsureSchemaAsync(connectionString);

    // WS-3 startup reconcile (S10): hydrate model_runtime_state from the runtime /api/tags so
    // bootstrap-installed models show `ready` without a manual verify. Failures signal (log +
    // Matrix) instead of silently degrading; startup never blocks on the runtime.
    await ReconcileModelRuntimeStateAtStartupAsync(app, connectionString);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(webClientCorsPolicy);
ApiRequestPipeline.Configure(app, authService, connectionString);

app.UseHangfireDashboard("/admin/jobs", new DashboardOptions
{
    Authorization = new[] { new PassThroughDashboardAuthorizationFilter() }
});

app.MapChannelEndpoints();
app.MapHealthEndpoints(databaseStatus);
app.MapAdminOperationEndpoints(applicationConfiguration, builder.Environment.ContentRootPath);
app.MapIngestionRunEndpoints();
app.MapDashboardEndpoints();
app.MapTranscriptEndpoints();
app.MapOverrideEndpoints();
app.MapAuthEndpoints(connectionString, useSecureCookies, jsonOptions, app.Logger);
app.MapSetupEndpoints(connectionString, jsonOptions);
app.MapOnboardingEndpoints(connectionString, jsonOptions);
app.MapSettingsEndpoints(connectionString, observabilityRuntime, app.Logger);
app.MapObservabilityEndpoints(connectionString, observabilityRuntime, app.Logger);
app.MapConfigEndpoints();
app.MapModelEndpoints(connectionString, jsonOptions);
app.MapSearchUiEndpoints(jsonOptions);
app.MapNoteEndpoints();

ApiRequestPipeline.MapReservedNotFoundFallbacks(app);
app.MapFallbackToFile("index.html");
app.Run();

static async Task ReconcileModelRuntimeStateAtStartupAsync(WebApplication app, string connectionString)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var reconcileService = scope.ServiceProvider.GetRequiredService<StreamingDigest.Application.ModelRuntimeReconcileService>();
        var repository = scope.ServiceProvider.GetRequiredService<StreamingDigest.Application.Repositories.IModelRuntimeStateRepository>();

        var result = await reconcileService.ReconcileAsync(repository);
        if (!result.RuntimeReachable)
        {
            var message = $"API startup degraded: model runtime reconcile could not reach /api/tags ({result.ErrorSummary}). Models will not show ready until the runtime is reachable and the app restarts.";
            app.Logger.LogWarning("{Message}", message);
            await TrySignalStartupIssueAsync(app, scope.ServiceProvider, message);
            return;
        }

        if (!result.Succeeded)
        {
            var message = $"API startup degraded: model runtime reconcile reached /api/tags but failed to persist state ({result.ErrorSummary}).";
            app.Logger.LogWarning("{Message}", message);
            await TrySignalStartupIssueAsync(app, scope.ServiceProvider, message);
            return;
        }

        app.Logger.LogInformation(
            "Model runtime startup reconcile completed: {PresentCount} present, {ReadyCount} marked ready, {InFlightCount} in-flight refreshed, {SkippedCount} skipped.",
            result.PresentCount,
            result.MarkedReadyCount,
            result.RefreshedInFlightCount,
            result.SkippedCount);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Model runtime startup reconcile failed unexpectedly; continuing startup.");
    }
}

static async Task TrySignalStartupIssueAsync(WebApplication app, IServiceProvider serviceProvider, string message)
{
    try
    {
        var notificationClient = serviceProvider.GetService<StreamingDigest.MatrixNotifier.MatrixNotificationClient>();
        if (notificationClient is null)
        {
            return;
        }

        var result = await notificationClient.SendTextMessageAsync(message);
        if (!result.Success)
        {
            app.Logger.LogInformation("Startup dependency warning was not sent to Matrix: {Reason}", result.Message);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Startup dependency warning could not be sent to Matrix.");
    }
}

public partial class Program;
