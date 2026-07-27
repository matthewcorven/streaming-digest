namespace StreamingDigest.Application.AudioToText;

/// <summary>
/// Input for an audio-to-text transcription request.
/// The caller is responsible for managing the lifetime of the file at <see cref="FilePath"/>;
/// temporary media lifecycle is owned by Task 6.4.
/// </summary>
public sealed record AudioTranscriptionRequest(
    /// <summary>Absolute path to the audio or video file to transcribe.</summary>
    string FilePath,
    /// <summary>Optional BCP-47 language hint (e.g. "en"). Provider may ignore it.</summary>
    string? LanguageHint = null);
