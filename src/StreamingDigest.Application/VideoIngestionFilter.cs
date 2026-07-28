namespace StreamingDigest.Application;

/// <summary>
/// Pure filtering and classification logic for video ingestion.
/// Implements the long-form selection rule (§3.2 DATA_MODEL: ingestion.minDurationSeconds)
/// and the max-age lookback cutoff (§3.2 DATA_MODEL: ingestion.defaultMaxAgeDays,
/// §3.5 DATA_MODEL: channels.default_max_age_days).
/// </summary>
public static class VideoIngestionFilter
{
    /// <summary>
    /// Classifies a video as long-form based on its duration and the configured minimum.
    /// </summary>
    /// <param name="durationSeconds">
    /// The video duration in seconds as reported by the platform, or <c>null</c> when unknown.
    /// </param>
    /// <param name="minDurationSeconds">
    /// The minimum duration threshold (inclusive) for a video to be considered long-form.
    /// Corresponds to the <c>ingestion.minDurationSeconds</c> app setting (default 61).
    /// </param>
    /// <returns>
    /// <c>true</c> when <paramref name="durationSeconds"/> is <c>null</c> (unknown — assume
    /// long-form until proven otherwise) or is greater than or equal to
    /// <paramref name="minDurationSeconds"/>; otherwise <c>false</c>.
    /// </returns>
    public static bool ClassifyIsLongForm(int? durationSeconds, int minDurationSeconds)
        => durationSeconds is null || durationSeconds.Value >= minDurationSeconds;

    /// <summary>
    /// Computes the earliest <c>published_at</c> a video must have to be eligible for
    /// ingestion from a given channel in the current run.
    /// </summary>
    /// <param name="now">The reference timestamp for the cutoff calculation (typically UTC now).</param>
    /// <param name="channelMaxAgeDays">
    /// The per-channel override, if any (<c>channels.default_max_age_days</c>).
    /// When present, this takes precedence over the global default.
    /// </param>
    /// <param name="globalDefaultMaxAgeDays">
    /// The global fallback (<c>ingestion.defaultMaxAgeDays</c>, default 30).
    /// Used when <paramref name="channelMaxAgeDays"/> is <c>null</c>.
    /// </param>
    /// <returns>
    /// <paramref name="now"/> minus the effective max-age window.
    /// </returns>
    public static DateTimeOffset ComputePublishedAfterCutoff(
        DateTimeOffset now,
        int? channelMaxAgeDays,
        int globalDefaultMaxAgeDays)
    {
        var effectiveDays = channelMaxAgeDays ?? globalDefaultMaxAgeDays;
        return now.AddDays(-effectiveDays);
    }
}
