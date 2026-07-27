namespace StreamingDigest.Application.Transcripts;

public sealed record TranscriptIngestionResult(
    bool Succeeded,
    Guid? TranscriptId,
    string? SourceType,
    string? LanguageCode,
    int CueCount,
    string? ErrorMessage,
    bool Skipped);
