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

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);