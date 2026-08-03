using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Observability;

namespace StreamingDigest.Infrastructure.AudioToText;

public sealed class LocalWhisperAudioToTextProvider(HttpClient httpClient, ILogger<LocalWhisperAudioToTextProvider> logger) : IAudioToTextProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await CorrelationContext.RunWithActivityAsync(
            "whisper.transcribe",
            async activity =>
            {
                activity?.SetTag("whisper.file_path", request.FilePath);
                activity?.SetTag("whisper.language_hint", request.LanguageHint);

                if (httpClient.BaseAddress is null)
                {
                    logger.LogWarning(
                        "LocalWhisperAudioToTextProvider is unconfigured; returning empty transcription for {FilePath}.",
                        request.FilePath);

                    return new AudioTranscriptionResult(
                        Engine: "whisper-unconfigured",
                        Model: "none",
                        Language: request.LanguageHint,
                        DurationSeconds: null,
                        FullText: string.Empty,
                        Cues: []);
                }

                var payload = new WhisperTranscriptionRequest(request.FilePath, request.LanguageHint);
                using var response = await httpClient.PostAsJsonAsync(
                    "/internal/audio-to-text/transcribe",
                    payload,
                    JsonOptions,
                    ct);
                response.EnsureSuccessStatusCode();

                var transcription = await response.Content.ReadFromJsonAsync<WhisperTranscriptionResponse>(JsonOptions, ct)
                    ?? throw new InvalidOperationException("The whisper service returned an empty payload.");

                return new AudioTranscriptionResult(
                    Engine: transcription.Engine,
                    Model: transcription.Model,
                    Language: transcription.Language,
                    DurationSeconds: transcription.DurationSeconds,
                    FullText: transcription.Text,
                    Cues: transcription.Cues?.Select(cue => new AudioTranscriptionCueDto(cue.StartSeconds, cue.EndSeconds, cue.Text)).ToArray() ?? []);
            },
            new Dictionary<string, object?>
            {
                ["whisper.file_path"] = request.FilePath,
                ["whisper.language_hint"] = request.LanguageHint
            },
            ActivityKind.Client);
    }

    public async Task<AudioToTextHealthResult> CheckHealthAsync(CancellationToken ct)
    {
        if (httpClient.BaseAddress is null)
        {
            // Truthful degrade: no whisper runtime is configured, so caption-less videos
            // cannot be transcribed. Captioned ingestion still proceeds with a warning
            // (PRD §2.4); the admin "test audio-to-text" op surfaces this honestly.
            return new AudioToTextHealthResult(
                IsHealthy: false,
                Engine: "whisper-unconfigured",
                Endpoint: null,
                Reason: "Audio-to-text is not configured. Set STREAMINGDIGEST_WHISPER_BASE_URL (or whisper:baseUrl) to the whisper service URL to enable caption-less transcription.");
        }

        try
        {
            using var response = await httpClient.GetAsync("/health", ct);
            if (response.IsSuccessStatusCode)
            {
                return new AudioToTextHealthResult(
                    IsHealthy: true,
                    Engine: "whisper",
                    Endpoint: httpClient.BaseAddress.ToString(),
                    Reason: $"Whisper service health check succeeded (HTTP {(int)response.StatusCode}).");
            }

            return new AudioToTextHealthResult(
                IsHealthy: false,
                Engine: "whisper",
                Endpoint: httpClient.BaseAddress.ToString(),
                Reason: $"Whisper service /health returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Whisper /health probe failed against {Endpoint}.", httpClient.BaseAddress);
            return new AudioToTextHealthResult(
                IsHealthy: false,
                Engine: "whisper",
                Endpoint: httpClient.BaseAddress.ToString(),
                Reason: $"Whisper service /health probe failed: {ex.Message}");
        }
    }

    private sealed record WhisperTranscriptionRequest(string FilePath, string? Language);

    private sealed record WhisperTranscriptionResponse(
        string Engine,
        string Model,
        string? Language,
        decimal? DurationSeconds,
        string Text,
        IReadOnlyList<WhisperTranscriptionCueResponse>? Cues);

    private sealed record WhisperTranscriptionCueResponse(decimal StartSeconds, decimal? EndSeconds, string Text);
}
