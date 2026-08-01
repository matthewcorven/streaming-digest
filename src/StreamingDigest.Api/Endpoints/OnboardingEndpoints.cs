using System.Text.Json;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.Api.Endpoints;

internal static class OnboardingEndpoints
{
    public static void MapOnboardingEndpoints(this WebApplication app, string connectionString, JsonSerializerOptions jsonOptions)
    {
        app.MapGet("/api/onboarding/status", async (HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(await readinessStateService.GetStatusAsync(connectionString, cancellationToken))
                : Results.Unauthorized());

        app.MapPost("/api/onboarding/steps/{stepKey}/verify", async (string stepKey, HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
        {
            if (!ApiRequestPipeline.IsAuthenticated(context.Request))
            {
                return Results.Unauthorized();
            }

            OnboardingStepVerificationRequest? request = null;
            if (context.Request.ContentLength is > 0)
            {
                try
                {
                    request = await JsonSerializer.DeserializeAsync<OnboardingStepVerificationRequest>(context.Request.Body, jsonOptions, cancellationToken);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "The request body is invalid." });
                }
            }

            var result = await readinessStateService.VerifyStepAsync(connectionString, stepKey, request, cancellationToken);
            return Results.Ok(result);
        });

        app.MapPost("/api/onboarding/complete-core-setup", async (HttpContext context, AppReadinessStateService readinessStateService, CancellationToken cancellationToken) =>
            ApiRequestPipeline.IsAuthenticated(context.Request)
                ? Results.Ok(await readinessStateService.CompleteCoreSetupAsync(connectionString, cancellationToken))
                : Results.Unauthorized());
    }
}