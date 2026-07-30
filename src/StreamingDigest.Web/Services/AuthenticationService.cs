using System.Net;
using System.Net.Http.Json;

namespace StreamingDigest.Web.Services;

public sealed class AuthenticationService
{
    private readonly HttpClient _httpClient;
    private bool _isAuthenticated;
    private string _currentUser = "guest";
    private string? _csrfToken;

    public AuthenticationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsAuthenticated => _isAuthenticated;

    public string CurrentUser => _currentUser;

    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureApiSessionAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            ResetAuthenticationState();
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ResetAuthenticationState();
            return false;
        }

        using var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!response.IsSuccessStatusCode)
        {
            ResetAuthenticationState();
            return false;
        }

        _isAuthenticated = true;
        _currentUser = username;
        _csrfToken = null;
        Changed?.Invoke();
        return true;
    }

    public async Task<string> EnsureApiSessionAsync(CancellationToken cancellationToken = default)
    {
        using var meResponse = await _httpClient.GetAsync("/api/auth/me", cancellationToken);
        if (!meResponse.IsSuccessStatusCode)
        {
            ResetAuthenticationState();
            throw new UnauthorizedAccessException("The API requires an authenticated session.");
        }

        _isAuthenticated = true;
        using var csrfResponse = await _httpClient.GetAsync("/api/auth/csrf", cancellationToken);
        if (!csrfResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The API could not obtain a CSRF token.");
        }

        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>(cancellationToken: cancellationToken);
        _csrfToken = csrf?.Token;
        if (string.IsNullOrWhiteSpace(_csrfToken))
        {
            throw new InvalidOperationException("The API could not obtain a CSRF token.");
        }

        return _csrfToken;
    }

    public async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (!request.Headers.TryGetValues("X-CSRF-Token", out var existingValues) || string.IsNullOrWhiteSpace(existingValues.FirstOrDefault()))
        {
            var csrfToken = await EnsureApiSessionAsync(cancellationToken);
            request.Headers.Add("X-CSRF-Token", csrfToken);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ResetAuthenticationState();
            throw new UnauthorizedAccessException("The API requires an authenticated session.");
        }

        return response;
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _httpClient.PostAsync("/api/auth/logout", null);
        }
        catch
        {
        }

        ResetAuthenticationState();
    }

    public void Logout() => _ = LogoutAsync();

    private void ResetAuthenticationState()
    {
        _isAuthenticated = false;
        _currentUser = "guest";
        _csrfToken = null;
        Changed?.Invoke();
    }

    private sealed class CsrfResponse
    {
        public string? Token { get; set; }
    }
}
