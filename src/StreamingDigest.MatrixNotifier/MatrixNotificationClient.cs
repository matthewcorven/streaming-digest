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
    private readonly Uri _cachedHomeserverUri;
    private readonly JsonSerializerOptions _jsonOptions;

    public MatrixNotificationClient(HttpClient httpClient, MatrixNotificationOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        ValidateOptions();

        _cachedHomeserverUri = ParseAndCacheHomeserverUri(options.HomeserverBaseUrl);
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = MatrixApiJsonContext.Default
        };
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

    private static Uri ParseAndCacheHomeserverUri(string homeserverBaseUrl)
    {
        var trimmedUrl = homeserverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"The configured Matrix homeserver URL '{trimmedUrl}' is invalid.");
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
                activity?.SetTag("messaging.system", "matrix");
                activity?.SetTag("messaging.operation", "send");
                activity?.SetTag("messaging.destination", effectiveRoomId);
                activity?.SetTag("messaging.enabled", _options.IsEnabled.ToString());

                if (!_options.IsEnabled)
                {
                    Debug.WriteLine("Matrix notifications are disabled.");
                    return new MatrixSendResult(false, "Matrix notifications are disabled.");
                }

                var requestUri = BuildSendMessageUri(effectiveRoomId);
                var requestBody = new MatrixTextMessage("m.text", message);

                activity?.SetTag("server.address", _options.HomeserverBaseUrl);
                activity?.SetTag("url.full", requestUri);

                using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
                request.Content = JsonContent.Create(requestBody, options: _jsonOptions);

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    Debug.WriteLine($"Matrix send failed: {response.StatusCode} {response.ReasonPhrase}");
                    throw new InvalidOperationException($"Matrix send failed with {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
                }

                var matrixResponse = await response.Content.ReadFromJsonAsync<MatrixSendResponse>(_jsonOptions, cancellationToken);
                var providerMessageId = matrixResponse?.EventId;

                Debug.WriteLine($"Matrix message sent successfully. Event ID: {providerMessageId}");
                return new MatrixSendResult(true, "Matrix message sent.", null, providerMessageId);
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

// Source-generated records for optimized JSON serialization
internal sealed record MatrixTextMessage(string msgtype, string body);

internal sealed record MatrixSendResponse([property: JsonPropertyName("event_id")] string? EventId);

// Source-generated serialization context for compile-time JSON optimization
[JsonSerializable(typeof(MatrixTextMessage))]
[JsonSerializable(typeof(MatrixSendResponse))]
internal partial class MatrixApiJsonContext : JsonSerializerContext
{
}
