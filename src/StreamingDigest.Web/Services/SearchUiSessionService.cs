using System.Net.Http.Json;

namespace StreamingDigest.Web.Services;

public sealed class SearchUiSessionService
{
    private readonly HttpClient _httpClient;

    public SearchUiSessionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> EnsureAuthenticatedSessionAsync(CancellationToken cancellationToken = default)
    {
        using var meResponse = await _httpClient.GetAsync("/api/auth/me", cancellationToken);
        if (!meResponse.IsSuccessStatusCode)
        {
            throw new UnauthorizedAccessException("The search UI requires an authenticated session.");
        }

        using var csrfResponse = await _httpClient.GetAsync("/api/auth/csrf", cancellationToken);
        if (!csrfResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The search UI could not obtain a CSRF token for the current session.");
        }

        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(csrf?.Token))
        {
            throw new InvalidOperationException("The search UI could not obtain a CSRF token for the current session.");
        }

        return csrf.Token;
    }

    private sealed class CsrfResponse
    {
        public string? Token { get; set; }
    }
}
