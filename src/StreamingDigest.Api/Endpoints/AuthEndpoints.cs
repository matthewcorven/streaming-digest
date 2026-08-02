using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, string connectionString, bool useSecureCookies, JsonSerializerOptions jsonOptions, ILogger logger)
    {
        app.MapPost("/api/auth/login", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
        {
            try
            {
                var requestBody = await JsonSerializer.DeserializeAsync<LoginRequest>(context.Request.Body, jsonOptions, cancellationToken);
                if (requestBody is null)
                {
                    return Results.BadRequest(new { error = "The request body is required." });
                }

                var result = await authService.AuthenticateAsync(
                    connectionString,
                    requestBody.Username ?? string.Empty,
                    requestBody.Password ?? string.Empty,
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken);

                if (!result.Success)
                {
                    return Results.Json(new { error = result.ErrorMessage }, statusCode: result.StatusCode);
                }

                context.Response.Cookies.Append("auth-session", result.SessionToken!, ApiRequestPipeline.CreateSessionCookieOptions(useSecureCookies));
                context.Response.Cookies.Append("csrf-token", result.CsrfToken!, ApiRequestPipeline.CreateCsrfCookieOptions(useSecureCookies));

                return Results.Ok(new { username = result.User!.Username, mustChangePassword = result.User.MustChangePassword });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Login request failed.");
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Login failed", detail: ex.Message);
            }
        });

        app.MapPost("/api/auth/logout", async (HttpContext context, AppAuthService authService, SearchUiService searchUiService, CancellationToken cancellationToken) =>
        {
            var sessionToken = context.Request.Cookies["auth-session"];
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                await authService.LogoutAsync(connectionString, sessionToken, cancellationToken);
            }

            searchUiService.RemoveState(ApiRequestPipeline.GetSearchUiStateKey(context));

            context.Response.Cookies.Delete("auth-session", new CookieOptions { Path = "/", HttpOnly = true, Secure = useSecureCookies, SameSite = SameSiteMode.Lax });
            context.Response.Cookies.Delete("csrf-token", new CookieOptions { Path = "/", HttpOnly = false, Secure = useSecureCookies, SameSite = SameSiteMode.Lax });

            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/auth/me", (HttpContext context) =>
        {
            var authenticatedUser = context.Items["AuthenticatedUser"] as AuthenticatedUser;
            return authenticatedUser is null
                ? Results.Unauthorized()
                : Results.Ok(new { username = authenticatedUser.Username, mustChangePassword = authenticatedUser.MustChangePassword });
        });

        app.MapGet("/api/auth/csrf", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
        {
            var csrfToken = string.Empty;
            var authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request, cancellationToken);
            if (authenticationResult.IsAuthenticated && !string.IsNullOrWhiteSpace(authenticationResult.CsrfToken))
            {
                csrfToken = authenticationResult.CsrfToken!;
            }

            if (string.IsNullOrWhiteSpace(csrfToken))
            {
                csrfToken = authService.CreateCsrfToken();
            }

            context.Response.Cookies.Append("csrf-token", csrfToken, ApiRequestPipeline.CreateCsrfCookieOptions(useSecureCookies));
            return Results.Ok(new { token = csrfToken });
        });

        app.MapPost("/api/auth/change-password", async (HttpContext context, AppAuthService authService, CancellationToken cancellationToken) =>
        {
            var authenticatedUser = context.Items["AuthenticatedUser"] as AuthenticatedUser;
            if (authenticatedUser is null)
            {
                return Results.Unauthorized();
            }

            var authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request, cancellationToken);
            if (!authenticationResult.IsAuthenticated || !ApiRequestPipeline.HasValidCsrfToken(context.Request, authenticationResult.CsrfToken))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var requestBody = await JsonSerializer.DeserializeAsync<ChangePasswordRequest>(context.Request.Body, jsonOptions, cancellationToken);
            if (requestBody is null || string.IsNullOrWhiteSpace(requestBody.CurrentPassword) || string.IsNullOrWhiteSpace(requestBody.NewPassword))
            {
                return Results.BadRequest(new { error = "Both current and new passwords are required." });
            }

            var changed = await authService.ChangePasswordAsync(connectionString, authenticatedUser.Id, requestBody.CurrentPassword, requestBody.NewPassword, cancellationToken);
            return changed
                ? Results.Ok(new { status = "updated" })
                : Results.BadRequest(new { error = "The current password is invalid or the new password is too weak." });
        });
    }
}