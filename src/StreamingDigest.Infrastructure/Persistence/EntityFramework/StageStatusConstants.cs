namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// Valid stage status values for ingestion items.
/// </summary>
public static class StageStatusConstants
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    /// <summary>
    /// The stage was deferred because a required model capability was unready
    /// (e.g. embeddings deferred until the model runtime is ready).
    /// </summary>
    public const string Deferred = "deferred";

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Skipped,
        Deferred
    };

    /// <summary>
    /// Validates that the provided status is a recognized value.
    /// </summary>
    /// <param name="status">Status to validate</param>
    /// <returns>True if status is valid, false otherwise</returns>
    public static bool IsValid(string? status)
        => status is not null && ValidStatuses.Contains(status);

    /// <summary>
    /// Validates and returns normalized status (lowercase).
    /// </summary>
    /// <param name="status">Status to normalize</param>
    /// <returns>Normalized status</returns>
    /// <exception cref="ArgumentException">If status is invalid</exception>
    public static string Normalize(string? status)
    {
        if (!IsValid(status))
        {
            throw new ArgumentException($"Invalid status value: '{status}'. Valid values are: {string.Join(", ", ValidStatuses)}", nameof(status));
        }

        return status!.ToLowerInvariant();
    }
}
