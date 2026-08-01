using StreamingDigest.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;

namespace StreamingDigest.Api;

internal static class ApiRequestPipeline
{
    public static void Configure(WebApplication app, AppAuthService authService, string connectionString)
    {
        app.UseHttpsRedirection();
        app.UseRouting();
        app.Use(RejectDirectSpaDocumentRequests);
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.Use((context, next) => AuthenticateRequestAsync(context, next, app, authService, connectionString));
    }

    public static bool IsAuthenticated(HttpRequest request)
    {
        return request.Cookies.ContainsKey("auth-session");
    }

    public static bool HasCsrfToken(HttpRequest request)
    {
        return request.Headers.ContainsKey("X-CSRF-Token") || request.Cookies.ContainsKey("csrf-token");
    }

    public static bool HasValidCsrfToken(HttpRequest request, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        var requestToken = request.Headers["X-CSRF-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(requestToken))
        {
            return string.Equals(requestToken, expectedToken, StringComparison.Ordinal);
        }

        return request.Cookies.TryGetValue("csrf-token", out var cookieToken) && string.Equals(cookieToken, expectedToken, StringComparison.Ordinal);
    }

    public static string GetSearchUiStateKey(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Request.Cookies["auth-session"]))
        {
            return $"session:{context.Request.Cookies["auth-session"]}";
        }

        var authenticatedUser = context.Items["AuthenticatedUser"] as AuthenticatedUser;
        if (!string.IsNullOrWhiteSpace(authenticatedUser?.Username))
        {
            return $"user:{authenticatedUser.Username}";
        }

        return "anonymous";
    }

    public static CookieOptions CreateSessionCookieOptions(bool useSecureCookies)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = useSecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        };
    }

    public static CookieOptions CreateCsrfCookieOptions(bool useSecureCookies)
    {
        return new CookieOptions
        {
            HttpOnly = false,
            Secure = useSecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        };
    }

    public static void MapReservedNotFoundFallbacks(WebApplication app)
    {
        var methods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD" };

        app.MapMethods("/api/{**catchAll}", methods, () => Results.NotFound());
        app.MapMethods("/admin/{**catchAll}", methods, () => Results.NotFound());
        app.MapMethods("/internal/{**catchAll}", methods, () => Results.NotFound());
    }

    private static Task RejectDirectSpaDocumentRequests(HttpContext context, Func<Task> next)
    {
        if (ShouldRejectDirectSpaDocumentRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next();
    }

    private static async Task AuthenticateRequestAsync(HttpContext context, Func<Task> next, WebApplication app, AppAuthService authService, string connectionString)
    {
        if (!RequiresAuthentication(context.Request))
        {
            await next();
            return;
        }

        RequestAuthenticationResult authenticationResult;
        try
        {
            authenticationResult = await authService.TryAuthenticateRequestAsync(connectionString, context.Request);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Authentication middleware failed while validating the request session.");
            authenticationResult = new RequestAuthenticationResult(false, null, null, false);
        }

        if (!authenticationResult.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (authenticationResult.RequiresPasswordChange && !ShouldAllowPasswordChangeFlow(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (RequiresCsrfProtection(context.Request) && !HasValidCsrfToken(context.Request, authenticationResult.CsrfToken))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Items["AuthenticatedUser"] = authenticationResult.User;
        await next();
    }

    private static bool RequiresAuthentication(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (path.Equals("/api/observability", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsHangfireDashboardPath(path))
        {
            return true;
        }

        return path.StartsWith("/api/internal", StringComparison.Ordinal)
            || path.StartsWith("/api/onboarding", StringComparison.Ordinal)
            || path.StartsWith("/api/settings", StringComparison.Ordinal)
            || path.StartsWith("/api/observability/toggle", StringComparison.Ordinal)
            || path.StartsWith("/api/config", StringComparison.Ordinal)
            || path.StartsWith("/api/auth/me", StringComparison.Ordinal)
            || path.StartsWith("/api/auth/change-password", StringComparison.Ordinal);
    }

    private static bool RequiresCsrfProtection(HttpRequest request)
    {
        if (IsHangfireDashboardPath(request.Path.Value ?? string.Empty))
        {
            return false;
        }

        return request.Method is not "GET" and not "HEAD" and not "OPTIONS" and not "TRACE";
    }

    private static bool IsHangfireDashboardPath(string path)
    {
        return path.Equals("/admin/jobs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin/jobs/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldAllowPasswordChangeFlow(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/auth/change-password", StringComparison.Ordinal)
            || path.StartsWith("/api/auth/me", StringComparison.Ordinal)
            || path.StartsWith("/api/auth/csrf", StringComparison.Ordinal)
            || path.StartsWith("/api/auth/logout", StringComparison.Ordinal);
    }

    private static bool ShouldRejectDirectSpaDocumentRequest(HttpRequest request)
    {
        var path = request.Path.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget ?? path;
        var rawPath = rawTarget.Split('?', 2)[0];
        var isReservedPath = IsReservedApplicationPath(request.Path);
        var isReservedRawPath = IsReservedApplicationPath(rawPath);

        if (IsDirectSpaDocumentPath(path) || (isReservedPath && IsDirectSpaDocumentPath(path)) || (isReservedRawPath && IsDirectSpaDocumentPath(rawPath)))
        {
            return true;
        }

        if (ContainsDotSegments(rawPath) && isReservedRawPath)
        {
            return true;
        }

        if (isReservedPath)
        {
            return false;
        }

        return false;
    }

    private static bool IsReservedApplicationPath(PathString path)
    {
        return path.StartsWithSegments("/api")
            || path.StartsWithSegments("/admin")
            || path.StartsWithSegments("/internal")
            || path.StartsWithSegments("/grafana")
            || path.StartsWithSegments("/pgadmin")
            || path.StartsWithSegments("/prometheus")
            || path.StartsWithSegments("/loki")
            || path.StartsWithSegments("/tempo");
    }

    private static bool IsReservedApplicationPath(string path)
    {
        return path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/grafana", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/pgadmin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/prometheus", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/loki", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/tempo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDotSegments(string path)
    {
        return path.Contains("../", StringComparison.Ordinal)
            || path.Contains("..\\", StringComparison.Ordinal)
            || path.EndsWith("/..", StringComparison.Ordinal)
            || path.EndsWith("\\..", StringComparison.Ordinal);
    }

    private static bool IsDirectSpaDocumentPath(string path)
    {
        return path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/index.htm", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/index.htm", StringComparison.OrdinalIgnoreCase);
    }
}