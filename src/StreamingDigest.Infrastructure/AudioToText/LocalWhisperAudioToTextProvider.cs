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
