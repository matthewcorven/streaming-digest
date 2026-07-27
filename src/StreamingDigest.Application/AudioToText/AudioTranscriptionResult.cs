namespace StreamingDigest.Application.AudioToText;

/// <summary>
/// Result of an audio-to-text transcription.  Matches the shape of the internal
/// <c>POST /internal/audio-to-text/transcribe</c> response (API spec §21).
/// </summary>
public sealed record AudioTranscriptionResult(
    string Engine,
    string Model,
    string? Language,
    decimal? DurationSeconds,
    string FullText,
    IReadOnlyList<AudioTranscriptionCueDto> Cues);
