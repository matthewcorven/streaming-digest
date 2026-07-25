using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

var connectionString = builder.Configuration.GetConnectionString("streamingdigest")
    ?? builder.Configuration.GetConnectionString("postgres")
    ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

var databaseStatus = await EnsureDatabaseConnectivityAsync(app.Logger, connectionString);

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
app.MapFallbackToFile("index.html");

app.Run();

static async Task<DatabaseStatus> EnsureDatabaseConnectivityAsync(ILogger logger, string connectionString)
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
            "API database connectivity confirmed for {Database} on {Host}:{Port}",
            databaseName,
            connectionStringBuilder.Host,
            connectionStringBuilder.Port);

        return new DatabaseStatus(true, databaseName, serverVersion);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "API could not connect to PostgreSQL; the API will continue in degraded mode");
        return new DatabaseStatus(false, connectionStringBuilder.Database ?? "postgres", "unavailable");
    }
}

public sealed record DatabaseStatus(bool Connected, string DatabaseName, string ServerVersion);

public partial class Program;
