using Microsoft.Extensions.Logging;
using StreamingDigest.Application.AudioToText;

namespace StreamingDigest.Infrastructure.AudioToText;

/// <summary>
/// No-op audio-to-text provider used in development until the real whisper adapter
/// (Task 6.3) is wired up.  Logs a warning and returns an empty result so that the
/// surrounding ingestion pipeline can handle the "no transcript available" path
/// without failing.
/// </summary>
public sealed class StubAudioToTextProvider(ILogger<StubAudioToTextProvider> logger) : IAudioToTextProvider
{
    public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
    {
        logger.LogWarning(
            "StubAudioToTextProvider is active; returning empty transcription for {FilePath}. "
            + "Replace with the real whisper adapter (Task 6.3) before using audio-to-text in production.",
            request.FilePath);

        return Task.FromResult(new AudioTranscriptionResult(
            Engine: "stub",
            Model: "stub",
            Language: request.LanguageHint,
            DurationSeconds: null,
            FullText: string.Empty,
            Cues: []));
    }

    public Task<AudioToTextHealthResult> CheckHealthAsync(CancellationToken ct)
    {
        // Truthful degrade: the stub is not a real runtime. Caption-less videos cannot be
        // transcribed; the admin op must report this honestly rather than faking "completed".
        return Task.FromResult(new AudioToTextHealthResult(
            IsHealthy: false,
            Engine: "stub",
            Endpoint: null,
            Reason: "Audio-to-text is using the stub provider; no whisper runtime is configured. Caption-less videos will degrade to 'unavailable_captions' with a notify event."));
    }
}
