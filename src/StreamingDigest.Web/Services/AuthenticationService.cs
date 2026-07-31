using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StreamingDigest.Web.Services;

public sealed class AuthenticationService
{
    private readonly HttpClient _httpClient;
    private bool _isAuthenticated;
    private bool _isSetupRequired;
    private bool _requiresPasswordChange;
    private string _currentUser = "guest";
    private string? _csrfToken;

    public AuthenticationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool IsAuthenticated => _isAuthenticated;

    public bool IsSetupRequired => _isSetupRequired;

    public bool RequiresPasswordChange => _requiresPasswordChange;

    public string CurrentUser => _currentUser;

    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshSetupStatusAsync(cancellationToken);
        if (_isSetupRequired)
        {
            ResetAuthenticationState();
            return;
        }

        try
        {
            await EnsureApiSessionAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            ResetAuthenticationState();
        }
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ResetAuthenticationState();
            return LoginResult.Failed;
        }

        using var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { username, password });
        if (!response.IsSuccessStatusCode)
        {
            ResetAuthenticationState();
            return LoginResult.Failed;
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
    _isSetupRequired = false;
        _isAuthenticated = true;
        _requiresPasswordChange = loginResponse?.MustChangePassword ?? false;
        _currentUser = loginResponse?.Username ?? username;
        _csrfToken = null;
        Changed?.Invoke();

        return _requiresPasswordChange ? LoginResult.RequiresPasswordChange : LoginResult.Success;
    }

    public async Task RefreshSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        var previousValue = _isSetupRequired;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status", cancellationToken);
            _isSetupRequired = response?.SetupRequired ?? false;
        }
        catch
        {
            _isSetupRequired = false;
        }

        if (previousValue != _isSetupRequired)
        {
            Changed?.Invoke();
        }
    }

    public async Task<FirstUserSetupResult> InitializeFirstUserAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/setup/initialize", new { username, password }, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _isSetupRequired = false;
            Changed?.Invoke();
            return new FirstUserSetupResult(FirstUserSetupResultType.Success, null);
        }

        var error = await TryReadJsonAsync<ApiErrorResponse>(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await RefreshSetupStatusAsync(cancellationToken);
            return new FirstUserSetupResult(FirstUserSetupResultType.AlreadyInitialized, error?.Error ?? "Setup has already been completed.");
        }

        return new FirstUserSetupResult(FirstUserSetupResultType.Failed, error?.Error ?? "The account could not be created.");
    }

    public async Task<string> EnsureApiSessionAsync(CancellationToken cancellationToken = default)
    {
        using var meResponse = await _httpClient.GetAsync("/api/auth/me", cancellationToken);
        if (!meResponse.IsSuccessStatusCode)
        {
            ResetAuthenticationState();
            throw new UnauthorizedAccessException("The API requires an authenticated session.");
        }

        var authResponse = await TryReadJsonAsync<AuthResponse>(meResponse, cancellationToken);
        _isAuthenticated = true;
        _requiresPasswordChange = authResponse?.MustChangePassword ?? false;
        _currentUser = authResponse?.Username ?? _currentUser;

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

    public async Task<T?> GetAuthenticatedJsonAsync<T>(string requestUri, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await SendAuthenticatedRequestAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await TryReadJsonAsync<T>(response, cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword })
        };

        using var response = await SendAuthenticatedRequestAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        _requiresPasswordChange = false;
        Changed?.Invoke();
        return true;
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
        _requiresPasswordChange = false;
        _currentUser = "guest";
        _csrfToken = null;
        Changed?.Invoke();
    }

    private async Task<T?> TryReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return default;
        }

        try
        {
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is not null && contentLength <= 0)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class CsrfResponse
    {
        public string? Token { get; set; }
    }

    private sealed class SetupStatusResponse
    {
        public bool SetupRequired { get; set; }
    }

    private sealed class ApiErrorResponse
    {
        public string? Error { get; set; }
    }

    private sealed record AuthResponse(string? Username, bool MustChangePassword);

    public sealed record FirstUserSetupResult(FirstUserSetupResultType ResultType, string? ErrorMessage);

    public enum FirstUserSetupResultType
    {
        Success,
        AlreadyInitialized,
        Failed
    }

    public enum LoginResult
    {
        Failed,
        Success,
        RequiresPasswordChange
    }
}
