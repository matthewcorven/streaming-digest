namespace StreamingDigest.Api.Endpoints;

internal sealed record VideoTranscriptResponse(
    Guid Id,
    Guid VideoId,
    string SourceType,
    string? LanguageCode,
    IReadOnlyList<TranscriptCueResponse> Cues);

internal sealed record TranscriptCueResponse(
    Guid Id,
    int Sequence,
    decimal StartSeconds,
    decimal? EndSeconds,
    string TextOriginal,
    string? TextOverride,
    string Text);