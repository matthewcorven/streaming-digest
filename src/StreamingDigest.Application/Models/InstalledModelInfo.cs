namespace StreamingDigest.Application.Models;

/// <summary>
/// Represents a model installed on the runtime, as reported by <c>/api/tags</c>.
/// </summary>
public sealed record InstalledModelInfo(
    string Name,
    string? ModelDigest,
    long? SizeBytes,
    DateTimeOffset? ModifiedAt);
