namespace StreamingDigest.Application.Transcripts;

public sealed record CaptionCueDto(int Sequence, decimal StartSeconds, decimal? EndSeconds, string Text);
