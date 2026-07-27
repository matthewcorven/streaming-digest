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
}
