using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Application.Observability;
using StreamingDigest.Domain;

namespace StreamingDigest.MatrixNotifier;

public sealed class MatrixNotificationClient
{
    private readonly HttpClient _httpClient;
    private readonly MatrixNotificationOptions _options;
    private readonly Uri? _cachedHomeserverUri;
    private readonly JsonSerializerOptions _jsonOptions;

    public MatrixNotificationClient(HttpClient httpClient, MatrixNotificationOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _cachedHomeserverUri = TryParseHomeserverUri(options.HomeserverBaseUrl);
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = MatrixApiJsonContext.Default
        };
    }

    private static Uri? TryParseHomeserverUri(string? homeserverBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(homeserverBaseUrl))
        {
            return null;
        }

        var trimmedUrl = homeserverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri;
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
                var health = GetHealth();
                activity?.SetTag("messaging.system", "matrix");
                activity?.SetTag("messaging.operation", "send");
                activity?.SetTag("messaging.destination", effectiveRoomId);
                activity?.SetTag("messaging.enabled", _options.IsEnabled.ToString());
                activity?.SetTag("messaging.status", health.Status);

                if (!_options.IsEnabled)
                {
                    Debug.WriteLine("Matrix notifications are disabled.");
                    return new MatrixSendResult(false, "Matrix notifications are disabled.");
                }

                if (!string.Equals(health.Status, "ready", StringComparison.Ordinal))
                {
                    Debug.WriteLine($"Matrix notifications are not ready: {health.Reason}");
                    return new MatrixSendResult(false, health.Reason);
                }

                var requestUri = BuildSendMessageUri(effectiveRoomId);
                var requestBody = new MatrixTextMessage("m.text", message);

                activity?.SetTag("server.address", _options.HomeserverBaseUrl);
                activity?.SetTag("url.full", requestUri);

                using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
                request.Content = JsonContent.Create(requestBody, options: _jsonOptions);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Matrix send failed: {response.StatusCode} {response.ReasonPhrase}");
                    throw new InvalidOperationException($"Matrix send failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
                }

                var matrixResponse = await response.Content.ReadFromJsonAsync<MatrixSendResponse>(_jsonOptions, cancellationToken);
                var providerMessageId = matrixResponse?.EventId;

                Debug.WriteLine($"Matrix message sent successfully. Event ID: {providerMessageId}");
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

    public MatrixHealthInfo GetHealth()
    {
        var enabled = _options.IsEnabled;
        var hasToken = !string.IsNullOrWhiteSpace(_options.AccessToken);
        var hasRoom = !string.IsNullOrWhiteSpace(_options.RoomId);
        var hasUrl = !string.IsNullOrWhiteSpace(_options.HomeserverBaseUrl);
        var hasValidUrl = _cachedHomeserverUri is not null;

        string status;
        string reason;
        if (!enabled)
        {
            status = "disabled";
            reason = "Matrix notifications are disabled.";
        }
        else if (!hasUrl)
        {
            status = "needs-config";
            reason = "Add a Matrix homeserver URL, access token, and room ID.";
        }
        else if (!hasValidUrl)
        {
            status = "invalid-url";
            reason = "Provide a valid Matrix homeserver URL.";
        }
        else if (hasToken && hasRoom)
        {
            status = "ready";
            reason = "Matrix notifications are configured.";
        }
        else if (!hasToken)
        {
            status = "needs-token";
            reason = "Add a Matrix access token to start sending.";
        }
        else if (!hasRoom)
        {
            status = "needs-room";
            reason = "Add a Matrix room ID to start sending.";
        }
        else
        {
            status = "needs-config";
            reason = "Add a Matrix homeserver URL, access token, and room ID.";
        }

        return new MatrixHealthInfo(enabled, status, reason, _options.HomeserverBaseUrl, _options.RoomId, _options.BotUserId);
    }

    private string BuildSendMessageUri(string roomId)
    {
        if (_cachedHomeserverUri is null)
        {
            throw new InvalidOperationException("Matrix homeserver URL is not configured correctly.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var escapedRoomId = Uri.EscapeDataString(roomId).Replace("%21", "!");
        var path = _cachedHomeserverUri.AbsolutePath.TrimEnd('/');
        return $"{_cachedHomeserverUri.Scheme}://{_cachedHomeserverUri.Host}{(_cachedHomeserverUri.IsDefaultPort ? string.Empty : $":{_cachedHomeserverUri.Port}")}{path}/_matrix/client/v3/rooms/{escapedRoomId}/send/m.room.message/{transactionId}";
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

public sealed record MatrixHealthInfo(
    bool Enabled,
    string Status,
    string Reason,
    string? HomeserverUrl,
    string? RoomId,
    string? BotUserId);

// Source-generated records for optimized JSON serialization
internal sealed record MatrixTextMessage(string msgtype, string body);

internal sealed record MatrixSendResponse([property: JsonPropertyName("event_id")] string? EventId);

// Source-generated serialization context for compile-time JSON optimization
[JsonSerializable(typeof(MatrixTextMessage))]
[JsonSerializable(typeof(MatrixSendResponse))]
internal partial class MatrixApiJsonContext : JsonSerializerContext
{
}
