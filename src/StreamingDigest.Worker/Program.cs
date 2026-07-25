using Npgsql;
using StreamingDigest.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var connectionString = builder.Configuration.GetConnectionString("streamingdigest")
    ?? builder.Configuration.GetConnectionString("postgres")
    ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

await EnsureDatabaseConnectivityAsync(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"), connectionString);

await host.RunAsync();

static async Task EnsureDatabaseConnectivityAsync(ILogger logger, string connectionString)
{
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

        logger.LogInformation(
            "Worker database connectivity confirmed for {Database} ({ServerVersion}) on {Host}:{Port}",
            databaseName,
            serverVersion,
            connectionStringBuilder.Host,
            connectionStringBuilder.Port);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Worker could not connect to PostgreSQL; the worker will continue in degraded mode");
    }
}
