namespace StreamingDigest.Domain;

public static class DomainEventTypeCatalog
{
    public const string ScreenshotFileMissing = "screenshot_file_missing";
    public const string TranscriptCutoverOverrideInert = "transcript_cutover_override_inert";
    public const string ScrapeExcluded = "scrape_excluded";
    public const string ScrapeFailed = "scrape_failed";
    public const string RateLimitDefermentCreated = "rate_limit_deferment_created";
    public const string RateLimitDefermentExpired = "rate_limit_deferment_expired";
    public const string RateLimitDefermentCleared = "rate_limit_deferment_cleared";
    public const string ChannelDegradedEntered = "channel_degraded_entered";
    public const string ChannelProbeSucceeded = "channel_probe_succeeded";
    public const string ChannelProbeFailed = "channel_probe_failed";
    public const string VideoUnavailableEntered = "video_unavailable_entered";
    public const string OrphanedNoteSurfaced = "orphaned_note_surfaced";
    public const string TempMediaOrphanCleanup = "temp_media_orphan_cleanup";
    public const string EmbeddingReprocessQueued = "embedding_reprocess_queued";
    public const string EmbeddingReprocessCompleted = "embedding_reprocess_completed";
    public const string EmbeddingReprocessFailed = "embedding_reprocess_failed";
    public const string NotificationDispatchOutcome = "notification_dispatch_outcome";
    public const string DigestAssembled = "digest_assembled";

    public static IReadOnlyList<string> All { get; } =
    [
        ScreenshotFileMissing,
        TranscriptCutoverOverrideInert,
        ScrapeExcluded,
        ScrapeFailed,
        RateLimitDefermentCreated,
        RateLimitDefermentExpired,
        RateLimitDefermentCleared,
        ChannelDegradedEntered,
        ChannelProbeSucceeded,
        ChannelProbeFailed,
        VideoUnavailableEntered,
        OrphanedNoteSurfaced,
        TempMediaOrphanCleanup,
        EmbeddingReprocessQueued,
        EmbeddingReprocessCompleted,
        EmbeddingReprocessFailed,
        NotificationDispatchOutcome,
        DigestAssembled
    ];

    public static bool IsDefined(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return All.Contains(eventType, StringComparer.Ordinal);
    }

    public static string RequireDefined(string? eventType)
    {
        if (IsDefined(eventType))
        {
            return eventType!;
        }

        throw new ArgumentException($"The domain event type '{eventType}' is not part of the catalog.", nameof(eventType));
    }
}
