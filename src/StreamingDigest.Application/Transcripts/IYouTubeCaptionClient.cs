namespace StreamingDigest.Application.Transcripts;

public interface IYouTubeCaptionClient
{
    Task<IReadOnlyList<CaptionTrackInfo>> GetAvailableTracksAsync(string videoId, CancellationToken ct);
    Task<CaptionFetchResult> FetchTrackAsync(string videoId, string trackCode, CancellationToken ct);
}
