namespace StreamingDigest.Application;

/// <summary>
/// Why a known video should be skipped when encountered during an ingestion run.
/// </summary>
public enum VideoSkipReason
{
    /// <summary>Do not skip — the video should be (re-)processed.</summary>
    None,

    /// <summary>
    /// The video has a terminal-success status (<c>processed</c> or
    /// <c>processed_with_warnings</c>) and the current run is not a
    /// <em>Reprocess</em> request.  Corresponds to the idempotency guard described in
    /// DATA_MODEL §3.6 and the vocabulary section ("Retry is idempotent").
    /// </summary>
    AlreadyProcessed,

    /// <summary>
    /// The video is in the <c>unavailable</c> terminal state — the platform reports it
    /// definitively gone.  Metadata retries stop regardless of the run type.
    /// Transition into this state is handled separately in Task 5.5.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Pure, stateless service that classifies whether a video should be skipped
/// during an ingestion run based on its current <c>ingestion_status</c>.
/// <para>
/// No persistence: the caller is responsible for reading the existing video record
/// and acting on the returned <see cref="VideoSkipReason"/>.
/// </para>
/// </summary>
public static class VideoIdempotencyService
{
    /// <summary>
    /// Returns the reason a video should be skipped, or <see cref="VideoSkipReason.None"/>
    /// when it should proceed through the pipeline.
    /// </summary>
    /// <param name="ingestionStatus">
    /// The current <c>videos.ingestion_status</c> value, or <c>null</c> when the video
    /// is not yet in the database (always returns <see cref="VideoSkipReason.None"/>).
    /// </param>
    /// <param name="isReprocessRequest">
    /// <c>true</c> when the caller explicitly requested a <em>Reprocess</em> — bypasses
    /// the idempotency guard for terminal-success videos.  Has no effect on
    /// <see cref="VideoSkipReason.Unavailable"/>.
    /// </param>
    public static VideoSkipReason ClassifySkipReason(string? ingestionStatus, bool isReprocessRequest = false)
    {
        if (IngestionStatuses.IsUnavailable(ingestionStatus))
        {
            return VideoSkipReason.Unavailable;
        }

        if (!isReprocessRequest && IngestionStatuses.IsTerminalSuccess(ingestionStatus))
        {
            return VideoSkipReason.AlreadyProcessed;
        }

        return VideoSkipReason.None;
    }
}
