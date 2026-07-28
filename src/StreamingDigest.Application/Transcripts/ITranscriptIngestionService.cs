namespace StreamingDigest.Application.Transcripts;

public interface ITranscriptIngestionService
{
    Task<TranscriptIngestionResult> IngestAsync(Guid videoId, CancellationToken ct);
}
