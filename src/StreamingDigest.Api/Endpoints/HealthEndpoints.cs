namespace StreamingDigest.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app, DatabaseStatus databaseStatus)
    {
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
    }
}