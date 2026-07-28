namespace StreamingDigest.Application.Transcripts;

public interface IVideoMediaSourceResolver
{
    Task<ResolvedMediaFile?> ResolveAsync(Guid videoId, CancellationToken cancellationToken);
}

public sealed record ResolvedMediaFile(string FilePath, bool DeleteWhenFinished = false);
