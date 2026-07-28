using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Pure, stateless service that encapsulates the terminal <em>Unavailable</em>
/// video-state transition.
/// <para>
/// Methods mutate the <see cref="Video"/> domain object in-place.
/// No persistence: the caller is responsible for saving the updated video via the
/// repository after a transition.
/// </para>
/// </summary>
public static class VideoUnavailableStateService
{
    /// <summary>
    /// Returns <c>true</c> when the video's ingestion status represents a terminal
    /// <em>Unavailable</em> state and the caller should skip further ingestion work.
    /// </summary>
    public static bool ShouldSkip(string? ingestionStatus)
        => IngestionStatuses.IsUnavailable(ingestionStatus);

    /// <summary>
    /// Marks the video as definitively unavailable on the upstream platform.
    /// Records the timestamp when metadata was last checked and confirmed absent.
    /// </summary>
    /// <param name="video">The video to mark unavailable.</param>
    /// <param name="now">The reference timestamp (UTC) for recording <c>metadata_fetched_at</c>.</param>
    public static void MarkUnavailable(Video video, DateTimeOffset now)
    {
        video.IngestionStatus = IngestionStatuses.Unavailable;
        video.MetadataFetchedAt = now;
    }
}
