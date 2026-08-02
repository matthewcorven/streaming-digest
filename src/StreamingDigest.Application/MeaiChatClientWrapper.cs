using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Application;

/// <summary>
/// Provides a simple wrapper for making chat requests to an Ollama-compatible LLM server via HTTP.
/// Used in services like DeterministicTranscriptChunkingService and LinkClassificationService.
/// Provides graceful error handling and JSON response parsing.
/// </summary>
/// <remarks>
/// This wrapper handles:
/// - Constructing Ollama-format chat requests (model, stream: false, format: "json", messages)
/// - Executing chat calls with timeout/exception handling
/// - Returning null on any failure (allowing services to fall back to deterministic/heuristic behavior)
/// - Parsing JSON responses with flexible schema support
/// </remarks>
public class MeaiChatClientWrapper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MeaiChatClientWrapper>? _logger;

    public MeaiChatClientWrapper(HttpClient httpClient, ILogger<MeaiChatClientWrapper>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    /// <summary>
    /// Sends a chat request with system and user messages, returning the assistant's response.
    /// Returns null if the call fails for any reason (network, timeout, malformed response).
    /// </summary>
    /// <param name="systemPrompt">System prompt guiding the LLM's behavior.</param>
    /// <param name="userPrompt">User query or request.</param>
    /// <param name="modelName">LLM model name (e.g., "llama2", "neural-chat"). Required.</param>
    /// <param name="temperature">Temperature for response generation (default 0.0 for deterministic).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assistant's response text, or null if the call fails.</returns>
    public virtual async Task<string?> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        string modelName,
        double temperature = 0.0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        try
        {
            var requestPayload = new
            {
                model = modelName,
                stream = false,
                format = "json",
                options = new { temperature },
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var response = await _httpClient.PostAsJsonAsync("api/chat", requestPayload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Ollama API returned status {StatusCode}", response.StatusCode);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                _logger?.LogWarning("Ollama API returned empty response");
                return null;
            }

            _logger?.LogDebug("Ollama chat completed successfully, response length: {ResponseLength}", responseBody.Length);
            return responseBody;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Ollama chat call was cancelled");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Ollama chat call failed with HTTP error");
            return null;
        }
        catch (TimeoutException ex)
        {
            _logger?.LogWarning(ex, "Ollama chat call timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ollama chat call failed unexpectedly");
            return null;
        }
    }

    /// <summary>
    /// Parses a JSON response from the chat client, extracting a field at the specified path.
    /// </summary>
    /// <param name="responseText">Raw response text from chat client.</param>
    /// <param name="fieldPath">Dot-separated path to the field (e.g., "segments" or "message.content").</param>
    /// <returns>The extracted JSON element, or null if parsing fails or path not found.</returns>
    public static JsonElement? TryExtractJsonField(string? responseText, string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(responseText) || string.IsNullOrWhiteSpace(fieldPath))
        {
            return null;
        }

        try
        {
            var normalized = responseText.Trim();
            
            // Handle markdown code fences
            if (normalized.StartsWith("```", StringComparison.Ordinal))
            {
                var fenceEnd = normalized.IndexOf("\n", StringComparison.Ordinal);
                if (fenceEnd >= 0)
                {
                    normalized = normalized[(fenceEnd + 1)..];
                }

                var closingFence = normalized.IndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0)
                {
                    normalized = normalized[..closingFence];
                }
            }

            // Extract JSON object
            var firstBrace = normalized.IndexOf('{');
            var lastBrace = normalized.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace <= firstBrace)
            {
                return null;
            }

            var jsonPayload = normalized[firstBrace..(lastBrace + 1)];
            using var document = JsonDocument.Parse(jsonPayload);
            var element = document.RootElement;

            // Navigate the field path
            var pathSegments = fieldPath.Split('.');
            foreach (var segment in pathSegments)
            {
                if (!element.TryGetProperty(segment, out var nextElement))
                {
                    return null;
                }

                element = nextElement;
            }

            // Return a cloned element (must be done before document disposal)
            return JsonSerializer.Deserialize<JsonElement?>(element.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

