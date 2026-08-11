using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StreamingDigest.Application.Observability;
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

    public Task<MatrixSendResult> SendTextMessageAsync(string message, CancellationToken cancellationToken)
        => SendTextMessageAsync(message, null, cancellationToken);

    public async Task<MatrixSendResult> SendTextMessageAsync(string message, string? roomOverride = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return await CorrelationContext.RunWithActivityAsync(
            "matrix.send",
            async activity =>
            {
                var effectiveRoomId = string.IsNullOrWhiteSpace(roomOverride) ? _options.RoomId : roomOverride;
                activity?.SetTag("messaging.system", "matrix");
                activity?.SetTag("messaging.operation", "send");
                activity?.SetTag("messaging.destination", effectiveRoomId);
                activity?.SetTag("messaging.enabled", _options.IsEnabled.ToString());

                if (!_options.IsEnabled)
                {
                    return new MatrixSendResult(false, "Matrix notifications are disabled.");
                }

                ValidateOptions();

                var requestUri = BuildSendMessageUri(effectiveRoomId);
                var requestBody = new
                {
                    msgtype = "m.text",
                    body = message
                };

                activity?.SetTag("server.address", _options.HomeserverBaseUrl);
                activity?.SetTag("url.full", requestUri);

                using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
                request.Content = JsonContent.Create(requestBody);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Matrix send failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
                }

                var providerMessageId = ExtractProviderMessageId(responseBody);
                return new MatrixSendResult(true, "Matrix message sent.", responseBody, providerMessageId);
            },
            new Dictionary<string, object?>
            {
                ["messaging.system"] = "matrix",
                ["messaging.destination"] = string.IsNullOrWhiteSpace(roomOverride) ? _options.RoomId : roomOverride,
                ["messaging.enabled"] = _options.IsEnabled
            },
            ActivityKind.Client);
    }

    public Task<MatrixSendResult> SendTestMessageAsync(CancellationToken cancellationToken = default)
        => SendTextMessageAsync("Streaming Digest Matrix notification test message.", cancellationToken: cancellationToken);

    private string BuildSendMessageUri(string roomId)
    {
        var homeserverBaseUrl = _options.HomeserverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(homeserverBaseUrl, UriKind.Absolute, out var homeserverUri))
        {
            throw new InvalidOperationException($"The configured Matrix homeserver URL '{homeserverBaseUrl}' is invalid.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var escapedRoomId = Uri.EscapeDataString(roomId).Replace("%21", "!");
        var path = homeserverUri.AbsolutePath.TrimEnd('/');
        return $"{homeserverUri.Scheme}://{homeserverUri.Host}{(homeserverUri.IsDefaultPort ? string.Empty : $":{homeserverUri.Port}")}{path}/_matrix/client/v3/rooms/{escapedRoomId}/send/m.room.message/{transactionId}";
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

    private static string? ExtractProviderMessageId(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("event_id", out var eventIdElement) && eventIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return eventIdElement.GetString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Ignore malformed provider payloads; the notification audit will rely on the response body if needed.
        }

        return null;
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

public sealed record MatrixSendResult(bool Success, string Message, string? ResponseBody = null, string? ProviderMessageId = null);
