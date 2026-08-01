using System.Text.Json;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.Api.Endpoints;

internal static class SetupEndpoints
{
    public static void MapSetupEndpoints(this WebApplication app, string connectionString, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/setup/status", async (FirstUserSetupService setupService, CancellationToken cancellationToken) =>
            Results.Ok(await setupService.GetStatusAsync(connectionString, cancellationToken)));

        app.MapPost("/api/setup/initialize", async (HttpContext context, FirstUserSetupService setupService, CancellationToken cancellationToken) =>
        {
            FirstUserInitializationRequest? requestBody;
            try
            {
                requestBody = await JsonSerializer.DeserializeAsync<FirstUserInitializationRequest>(context.Request.Body, jsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "The request body is invalid." });
            }

            if (requestBody is null)
            {
                return Results.BadRequest(new { error = "The request body is required." });
            }

            var result = await setupService.InitializeAsync(
                connectionString,
                requestBody.Username ?? string.Empty,
                requestBody.Password ?? string.Empty,
                cancellationToken);

            return result.Outcome switch
            {
                FirstUserInitializationOutcome.Success => Results.Created("/login", new { username = result.Username }),
                FirstUserInitializationOutcome.AlreadyInitialized => Results.Conflict(new { error = result.ErrorMessage }),
                FirstUserInitializationOutcome.InvalidUsername => Results.BadRequest(new { error = result.ErrorMessage }),
                FirstUserInitializationOutcome.WeakPassword => Results.BadRequest(new { error = result.ErrorMessage }),
                _ => Results.BadRequest(new { error = "The request could not be completed." })
            };
        });
    }
}

public sealed record FirstUserInitializationRequest(string? Username, string? Password);