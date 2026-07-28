namespace StreamingDigest.Application.AudioToText;

public sealed record AudioTranscriptionCueDto(
    decimal StartSeconds,
    decimal? EndSeconds,
    string Text);
