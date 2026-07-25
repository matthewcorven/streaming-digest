using System.Net.Http.Headers;
using System.Net.Http.Json;
using StreamingDigest.Domain;

namespace StreamingDigest.MatrixNotifier;

public sealed class MatrixNotificationClient
{
    private readonly HttpClient _httpClient;
    private readonly MatrixNotificationOptions _options;

    public MatrixNotificationClient(HttpClient httpClient, MatrixNotificationOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string BuildPreview(Video video, string message)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return $"[matrix] {video.DisplayTitle}: {message}";
    }

    public async Task<MatrixSendResult> SendTextMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!_options.IsEnabled)
        {
            return new MatrixSendResult(false, "Matrix notifications are disabled.");
        }

        ValidateOptions();

        var requestUri = BuildSendMessageUri();
        var requestBody = new
        {
            msgtype = "m.text",
            body = message
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Matrix send failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        return new MatrixSendResult(true, "Matrix message sent.", responseBody);
    }

    public Task<MatrixSendResult> SendTestMessageAsync(CancellationToken cancellationToken = default)
        => SendTextMessageAsync("Streaming Digest Matrix notification test message.", cancellationToken);

    private string BuildSendMessageUri()
    {
        var homeserverBaseUrl = _options.HomeserverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(homeserverBaseUrl, UriKind.Absolute, out var homeserverUri))
        {
            throw new InvalidOperationException($"The configured Matrix homeserver URL '{homeserverBaseUrl}' is invalid.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var roomId = Uri.EscapeDataString(_options.RoomId).Replace("%21", "!");
        var path = homeserverUri.AbsolutePath.TrimEnd('/');
        return $"{homeserverUri.Scheme}://{homeserverUri.Host}{(homeserverUri.IsDefaultPort ? string.Empty : $":{homeserverUri.Port}")}{path}/_matrix/client/v3/rooms/{roomId}/send/m.room.message/{transactionId}";
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.HomeserverBaseUrl))
        {
            throw new InvalidOperationException("A Matrix homeserver URL is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            throw new InvalidOperationException("A Matrix access token is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.RoomId))
        {
            throw new InvalidOperationException("A Matrix room ID is required.");
        }
    }
}

public sealed class MatrixNotificationOptions
{
    public bool IsEnabled { get; init; }
    public string HomeserverBaseUrl { get; init; } = "https://matrix-client.matrix.org";
    public string AccessToken { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public string? BotUserId { get; init; }
    public bool OnManualRuns { get; init; } = true;
    public bool OnScheduledRuns { get; init; } = true;
    public bool OnBackfillRuns { get; init; }
    public string DashboardBaseUrl { get; init; } = "http://localhost:8080";
}

public sealed record MatrixSendResult(bool Success, string Message, string? ResponseBody = null);
