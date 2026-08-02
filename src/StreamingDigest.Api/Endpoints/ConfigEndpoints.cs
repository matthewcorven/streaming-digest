namespace StreamingDigest.Api.Endpoints;

internal static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config/runtime", (HttpContext context) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(new
                {
                    environment = app.Environment.EnvironmentName,
                    deployMode = "local",
                    features = new[] { "settings", "models" }
                })
                : Results.Unauthorized());

        app.MapGet("/api/config/schema", (HttpContext context) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
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
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(new { valid = true, warnings = Array.Empty<string>() })
                : Results.Unauthorized());
    }
}