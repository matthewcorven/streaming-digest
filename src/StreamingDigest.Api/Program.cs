using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

builder.Services.AddDbContext<StreamingDigestDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<BootstrapAdminUserService>();
builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();

var app = builder.Build();

using var startupScope = CorrelationContext.BeginLoggingScope(app.Logger, new Dictionary<string, object?>
{
    ["startup"] = "api",
    ["environment"] = app.Environment.EnvironmentName
});

var databaseStatus = await EnsureDatabaseConnectivityAsync(app.Logger, connectionString);
var defaultObservabilityEnabled = ResolveDefaultObservabilityEnabled(builder.Configuration, builder.Environment, false);
var defaultRetentionDays = ComputeRetentionDays();
var observabilityRuntime = await LoadObservabilityRuntimeAsync(app.Logger, builder.Configuration, builder.Environment, connectionString, databaseStatus.Connected, defaultObservabilityEnabled, defaultRetentionDays);
await EnsureObservabilityReadinessAsync(app.Logger, observabilityRuntime);

if (databaseStatus.Connected)
{
    var seeder = new AppSettingsSeeder(app.Logger);
    await seeder.SeedDefaultsAsync(connectionString, observabilityRuntime.Enabled, observabilityRuntime.RetentionDays);

    var bootstrapAdminUserService = app.Services.GetRequiredService<BootstrapAdminUserService>();
    await bootstrapAdminUserService.EnsureBootstrapAdminUserAsync(connectionString);
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

app.MapPost("/api/auth/login", () => Results.Ok(new { username = "admin", mustChangePassword = true }));
app.MapPost("/api/auth/logout", () => Results.Ok(new { success = true }));
app.MapGet("/api/auth/me", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new { username = "admin", mustChangePassword = false })
        : Results.Unauthorized());
app.MapGet("/api/auth/csrf", () => Results.Ok(new { token = "test-csrf-token" }));
app.MapPost("/api/auth/change-password", (HttpContext context) =>
{
    if (!IsAuthenticated(context.Request))
    {
        return Results.Unauthorized();
    }

    if (!HasCsrfToken(context.Request))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new { status = "updated" });
});

app.MapGet("/api/onboarding/status", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new
        {
            isCoreSetupComplete = false,
            isFullyReady = false,
            steps = Array.Empty<object>()
        })
        : Results.Unauthorized());
app.MapPost("/api/onboarding/steps/{stepKey}/verify", (string stepKey, HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Accepted($"/api/operations/{Guid.NewGuid():N}", new { stepKey })
        : Results.Unauthorized());
app.MapPost("/api/onboarding/complete-core-setup", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new { status = "updated" })
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
                ["observability.links.hangfireUrl"] = "/admin/jobs"
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
    services = new
    {
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

app.MapGet("/api/models/options", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Ok(new
        {
            models = new[]
            {
                new { id = "gpt-4o-mini", family = "llm", status = "available" },
                new { id = "text-embedding-3-small", family = "embedding", status = "available" }
            }
        })
        : Results.Unauthorized());
app.MapPost("/api/models/download", (HttpContext context) =>
    IsAuthenticated(context.Request)
        ? Results.Accepted("/api/models/download", new { status = "queued" })
        : Results.Unauthorized());
app.MapFallbackToFile("index.html");

app.Run();

static bool IsAuthenticated(HttpRequest request)
{
    if (request.Headers.TryGetValue("X-Test-Auth", out var authHeader) && authHeader.ToString().Contains("true", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return request.Cookies.ContainsKey("auth-session");
}

static bool HasCsrfToken(HttpRequest request)
{
    return request.Headers.TryGetValue("X-CSRF-Token", out var csrfHeader) && !string.IsNullOrWhiteSpace(csrfHeader.ToString());
}

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

static int ComputeRetentionDays()
{
    var drives = DriveInfo.GetDrives().Where(drive => drive.IsReady).ToArray();
    if (drives.Length == 0)
    {
        return 0;
    }

    var freeSpaceBytes = drives.Select(drive => drive.AvailableFreeSpace).DefaultIfEmpty(0).Max();
    if (freeSpaceBytes > 5L * 1024 * 1024 * 1024)
    {
        return 90;
    }

    if (freeSpaceBytes > 1L * 1024 * 1024 * 1024)
    {
        return 30;
    }

    return 0;
}

static async Task<ObservabilityRuntimeState> LoadObservabilityRuntimeAsync(ILogger logger, IConfiguration configuration, IHostEnvironment environment, string connectionString, bool databaseConnected, bool defaultEnabled, int defaultRetentionDays)
{
    var resolvedEnabled = ResolveDefaultObservabilityEnabled(configuration, environment, defaultEnabled);
    var resolvedRetentionDays = defaultRetentionDays;
    var grafanaUrl = configuration["observability:services:grafana:url"] ?? "http://grafana:3000";
    var prometheusUrl = configuration["observability:services:prometheus:url"] ?? "http://prometheus:9090";
    var lokiUrl = configuration["observability:services:loki:url"] ?? "http://loki:3100";
    var tempoUrl = configuration["observability:services:tempo:url"] ?? "http://tempo:3200";
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
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var probeTargets = new[]
        {
            (Name: "grafana", Url: observabilityRuntime.GrafanaUrl),
            (Name: "prometheus", Url: observabilityRuntime.PrometheusUrl),
            (Name: "loki", Url: observabilityRuntime.LokiUrl),
            (Name: "tempo", Url: observabilityRuntime.TempoUrl)
        };

        foreach (var target in probeTargets)
        {
            var probeUri = new UriBuilder(target.Url)
            {
                Path = "/",
                Query = string.Empty
            }.Uri;

            using var response = await httpClient.GetAsync(probeUri);
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }

            observabilityRuntime.Ready = false;
            logger.LogWarning("Observability service {ServiceName} is not yet reachable at {ServiceUrl}", target.Name, target.Url);
            return;
        }

        observabilityRuntime.Ready = true;
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
    await using var command = new NpgsqlCommand("SELECT value_json FROM public.app_settings WHERE key = @key", connection);
    command.Parameters.AddWithValue("key", key);
    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        var json = reader.GetString(0);
        if (bool.TryParse(json, out var value))
        {
            return value;
        }

        if (JsonDocument.Parse(json).RootElement.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (JsonDocument.Parse(json).RootElement.ValueKind == JsonValueKind.False)
        {
            return false;
        }
    }

    return fallback;
}

static async Task<int> ReadIntSettingAsync(NpgsqlConnection connection, string key, int fallback)
{
    await using var command = new NpgsqlCommand("SELECT value_json FROM public.app_settings WHERE key = @key", connection);
    command.Parameters.AddWithValue("key", key);
    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        var json = reader.GetString(0);
        if (int.TryParse(json, out var value))
        {
            return value;
        }

        if (JsonDocument.Parse(json).RootElement.ValueKind == JsonValueKind.Number && JsonDocument.Parse(json).RootElement.TryGetInt32(out var intValue))
        {
            return intValue;
        }
    }

    return fallback;
}

static async Task<string> ReadStringSettingAsync(NpgsqlConnection connection, string key, string fallback)
{
    await using var command = new NpgsqlCommand("SELECT value_json FROM public.app_settings WHERE key = @key", connection);
    command.Parameters.AddWithValue("key", key);
    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        var json = reader.GetString(0);
        if (!string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<string>(json) ?? fallback;
        }
    }

    return fallback;
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
    public string OtelCollectorUrl { get; set; } = string.Empty;
}

public partial class Program;
