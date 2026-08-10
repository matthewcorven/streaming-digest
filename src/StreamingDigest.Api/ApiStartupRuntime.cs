using System.Diagnostics;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using Npgsql;
using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Observability;

namespace StreamingDigest.Api;

internal static class ApiStartupRuntime
{
    public static async Task<ApiStartupState> InitializeInfrastructureAsync(WebApplicationBuilder builder, ApplicationConfiguration applicationConfiguration)
    {
        var connectionString = ResolveConnectionString(builder.Configuration, applicationConfiguration);

        using var startupLoggerFactory = CreateStartupLoggerFactory();
        var startupLogger = startupLoggerFactory.CreateLogger("Startup");
        var databaseStatus = await EnsureDatabaseConnectivityAsync(startupLogger, connectionString);
        var hangfireStorage = CreateHangfireStorage(startupLogger, connectionString, databaseStatus.Connected);

        if (!databaseStatus.Connected)
        {
            startupLogger.LogWarning("Hangfire PostgreSQL connectivity is unavailable; the dashboard will use in-memory storage for this startup.");
        }

        return new ApiStartupState(connectionString, databaseStatus, hangfireStorage);
    }

    public static void ConfigureOtlpExporter(OtlpExporterOptions options)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")
            ?? "http://localhost:18889";

        options.Protocol = OtlpExportProtocol.Grpc;
        options.Endpoint = new Uri(endpoint);
    }

    private static string ResolveConnectionString(IConfiguration configuration, ApplicationConfiguration applicationConfiguration)
    {
        return configuration.GetConnectionString("streamingdigest")
            ?? configuration.GetConnectionString("postgres")
            ?? applicationConfiguration.ConnectionStrings.StreamingDigest;
    }

    private static ILoggerFactory CreateStartupLoggerFactory()
    {
        return LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
    }

    private static async Task<DatabaseStatus> EnsureDatabaseConnectivityAsync(ILogger logger, string connectionString)
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

    private static JobStorage CreateHangfireStorage(ILogger logger, string connectionString, bool databaseConnected)
    {
        if (!databaseConnected)
        {
            return new MemoryStorage();
        }

        // Hangfire.PostgreSql's PostgreSqlObjectsInstaller static constructor resolves a logger
        // through Hangfire's process-global LogProvider. Hangfire.NetCore's AspNetCoreLogProvider
        // captures the ILoggerFactory of the FIRST host that configured it; once that host is
        // disposed (WebApplicationFactory teardown in integration tests) the cctor throws
        // ObjectDisposedException and the failed type initializer is cached for the whole
        // process, silently forcing every later host onto MemoryStorage. Installing a no-op
        // provider before storage creation keeps the installer cctor from ever touching a
        // disposed factory. The API's real logging is unaffected (it flows through ILogger).
        Hangfire.Logging.LogProvider.SetCurrentLogProvider(new NoOpHangfireLogProvider());
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
}

internal sealed record ApiStartupState(string ConnectionString, DatabaseStatus DatabaseStatus, JobStorage HangfireStorage);

/// <summary>
/// No-op Hangfire log provider used to isolate PostgreSqlObjectsInstaller's static constructor
/// from a disposed ILoggerFactory held by Hangfire.NetCore's AspNetCoreLogProvider.
/// </summary>
internal sealed class NoOpHangfireLogProvider : Hangfire.Logging.ILogProvider
{
    public Hangfire.Logging.ILog GetLogger(string name) => new NoOpHangfireLog();

    private sealed class NoOpHangfireLog : Hangfire.Logging.ILog
    {
        public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null) => true;
    }
}

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);