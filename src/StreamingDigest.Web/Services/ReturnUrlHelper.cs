namespace StreamingDigest.Web.Services;

public static class ReturnUrlHelper
{
    public static string BuildLoginRedirectTarget(string? relativeUri)
    {
        var safeReturnUrl = ResolveSafeReturnUrl(relativeUri);
        if (safeReturnUrl is null)
        {
            return "/login";
        }

        return $"/login?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
    }

    public static string? ResolveSafeReturnUrl(string? relativeUri)
    {
        if (string.IsNullOrWhiteSpace(relativeUri))
        {
            return null;
        }

        var trimmed = relativeUri.Trim();
        var normalized = trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }
}