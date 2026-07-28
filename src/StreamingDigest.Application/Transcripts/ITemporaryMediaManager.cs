namespace StreamingDigest.Application.Transcripts;

public interface ITemporaryMediaManager
{
    Task<string> CreateTemporaryMediaAsync(string sourceFilePath, CancellationToken cancellationToken);
    Task DeleteTemporaryMediaAsync(string? filePath, CancellationToken cancellationToken);
}
