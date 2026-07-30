namespace StreamingDigest.Web.Services;

public sealed class SearchUiSessionService
{
    private readonly AuthenticationService _authenticationService;

    public SearchUiSessionService(AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public Task<string> EnsureAuthenticatedSessionAsync(CancellationToken cancellationToken = default)
        => _authenticationService.EnsureApiSessionAsync(cancellationToken);

    public Task<HttpResponseMessage> SendAuthenticatedRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        => _authenticationService.SendAuthenticatedRequestAsync(request, cancellationToken);
}
