namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Canonical per-stage name constants tracked on <c>ingestion_items</c>
/// (A1 per-stage status columns) and used for retry targeting.
/// </summary>
public static class IngestionStageNames
{
    public const string Transcript = "transcript";
    public const string Segments = "segments";
    public const string Screenshots = "screenshots";
    public const string Links = "links";
    public const string Repos = "repos";
    public const string Websites = "websites";
    public const string Embeddings = "embeddings";

    public static readonly IReadOnlyList<string> All =
    [
        Transcript,
        Segments,
        Screenshots,
        Links,
        Repos,
        Websites,
        Embeddings,
    ];
}

/// <summary>
/// Canonical per-stage status values written to <c>ingestion_items</c> stage columns.
/// Aligned with the persisted vocabulary enforced by
/// <c>StageStatusConstants</c> in Infrastructure (<c>completed</c> marks a finished stage).
/// </summary>
public static class IngestionStageStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    /// <summary>
    /// The stage was deferred because a required model capability was unready
    /// (e.g. embeddings deferred with <c>embedding_status=deferred</c>).
    /// </summary>
    public const string Deferred = "deferred";
}
