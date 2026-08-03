namespace StreamingDigest.Application.Models;

/// <summary>
/// Streamed progress from <c>/api/pull</c>. The <c>status</c> field conveys the
/// current phase (e.g. "pulling manifest", "downloading digest", "verifying sha256",
/// "writing manifest", "success"). When <c>total</c> and <c>completed</c> are both
/// present they represent byte-level progress for the current <c>digest</c> being downloaded.
/// </summary>
public sealed record PullProgress(
    string Status,
    string? Digest = null,
    long? Total = null,
    long? Completed = null);
