namespace StreamingDigest.Application;

/// <summary>
/// Canonical string constants for <c>videos.ingestion_status</c> (§3.6 DATA_MODEL).
/// </summary>
public static class IngestionStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Processed = "processed";
    public const string ProcessedWithWarnings = "processed_with_warnings";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    /// <summary>
    /// Terminal state: the platform reports the video definitively gone (deleted/private).
    /// Metadata retries stop; all stored artefacts are preserved.
    /// Handled as a separate transition in Task 5.5.
    /// </summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// Returns <c>true</c> for statuses that represent a successfully-completed pipeline
    /// and therefore trigger the idempotency guard (skip unless explicitly reprocessing).
    /// </summary>
    public static bool IsTerminalSuccess(string? status)
        => status is Processed or ProcessedWithWarnings;

    /// <summary>
    /// Returns <c>true</c> when the video has been reported definitively gone by the platform.
    /// </summary>
    public static bool IsUnavailable(string? status)
        => status is Unavailable;
}
