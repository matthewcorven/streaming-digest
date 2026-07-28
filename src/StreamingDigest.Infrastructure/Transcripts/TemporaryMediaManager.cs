using StreamingDigest.Application.Transcripts;

namespace StreamingDigest.Infrastructure.Transcripts;

public sealed class TemporaryMediaManager : ITemporaryMediaManager
{
    public Task<string> CreateTemporaryMediaAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"The source media file '{sourceFilePath}' does not exist.", sourceFilePath);
        }

        var extension = Path.GetExtension(sourceFilePath);
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"streaming-digest-{Guid.NewGuid():N}{extension}");
        File.Copy(sourceFilePath, tempFilePath, overwrite: true);

        return Task.FromResult(tempFilePath);
    }

    public Task DeleteTemporaryMediaAsync(string? filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
