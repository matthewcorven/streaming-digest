using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Transcripts;

namespace StreamingDigest.Infrastructure.Transcripts;

public sealed class StubYouTubeCaptionClient(ILogger<StubYouTubeCaptionClient> logger) : IYouTubeCaptionClient
{
    public Task<IReadOnlyList<CaptionTrackInfo>> GetAvailableTracksAsync(string videoId, CancellationToken ct)
    {
        logger.LogWarning("StubYouTubeCaptionClient is active; returning no caption tracks for video {VideoId}.", videoId);
        return Task.FromResult<IReadOnlyList<CaptionTrackInfo>>([]);
    }

    public Task<CaptionFetchResult> FetchTrackAsync(string videoId, string trackCode, CancellationToken ct)
    {
        logger.LogWarning("StubYouTubeCaptionClient is active; returning an empty caption payload for video {VideoId} track {TrackCode}.", videoId, trackCode);
        return Task.FromResult(new CaptionFetchResult(null, false, string.Empty, []));
    }
}
