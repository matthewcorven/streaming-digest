namespace StreamingDigest.Application.AudioToText;

/// <summary>
/// Abstraction over the audio-to-text service (whisper.cpp or compatible).
/// Implementations hide all concrete engine / HTTP details from application services.
/// The concrete whisper HTTP adapter is provided by Task 6.3.
/// </summary>
public interface IAudioToTextProvider
{
    /// <summary>Transcribe the audio file identified in <paramref name="request"/>.</summary>
    Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct);

    /// <summary>
    /// Probe the underlying audio-to-text runtime health (e.g. whisper <c>GET /health</c>).
    /// Returns a truthful <see cref="AudioToTextHealthResult"/> — never throws — so the admin
    /// "test audio-to-text" operation can report a real status instead of a fake "completed".
    /// </summary>
    Task<AudioToTextHealthResult> CheckHealthAsync(CancellationToken ct);
}

/// <summary>
/// Result of an audio-to-text health probe. <see cref="IsHealthy"/> is true only when the
/// runtime answered the probe; an unconfigured or stub provider reports <c>false</c> with a
/// human-readable <see cref="Reason"/> so callers can degrade truthfully (prevent/notify).
/// </summary>
public sealed record AudioToTextHealthResult(
    bool IsHealthy,
    string? Engine,
    string? Endpoint,
    string Reason);
