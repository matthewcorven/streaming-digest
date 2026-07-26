namespace StreamingDigest.Web.Services;

public sealed class AuthenticationService
{
    private bool _isAuthenticated;
    private string _currentUser = "admin";

    public bool IsAuthenticated => _isAuthenticated;

    public string CurrentUser => _currentUser;

    public event Action? Changed;

    public Task<bool> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _isAuthenticated = false;
            Changed?.Invoke();
            return Task.FromResult(false);
        }

        var isValidLogin = (username.Equals("admin", StringComparison.OrdinalIgnoreCase) || username.Equals("demo", StringComparison.OrdinalIgnoreCase))
            && (password.Equals("admin", StringComparison.OrdinalIgnoreCase) || password.Equals("demo", StringComparison.OrdinalIgnoreCase));

        _currentUser = isValidLogin ? username : "guest";
        _isAuthenticated = isValidLogin;
        Changed?.Invoke();
        return Task.FromResult(isValidLogin);
    }

    public void Logout()
    {
        _isAuthenticated = false;
        _currentUser = "guest";
        Changed?.Invoke();
    }
}
