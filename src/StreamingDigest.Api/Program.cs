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
    logging.AddOtlpExporter(options => ApiStartupRuntime.ConfigureOtlpExporter(options));
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddSource(CorrelationContext.ActivitySourceName);
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddOtlpExporter(options => ApiStartupRuntime.ConfigureOtlpExporter(options));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CorrelationContext.ActivitySourceName);
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
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

ApiRequestPipeline.Configure(app, authService, connectionString);

app.UseHangfireDashboard("/admin/jobs", new DashboardOptions
{
    Authorization = new[] { new PassThroughDashboardAuthorizationFilter() }
});

app.MapChannelEndpoints();
app.MapHealthEndpoints(databaseStatus);
app.MapAdminOperationEndpoints(applicationConfiguration, builder.Environment.ContentRootPath);
app.MapIngestionRunEndpoints();
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

public partial class Program;
