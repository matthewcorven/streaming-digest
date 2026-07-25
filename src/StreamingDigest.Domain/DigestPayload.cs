using System.Text.Json;

namespace StreamingDigest.Domain;

public static class DigestPayloadSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(DigestPayload payload)
        => JsonSerializer.Serialize(payload, SerializerOptions);

    public static DigestPayload Deserialize(string payloadJson)
        => JsonSerializer.Deserialize<DigestPayload>(payloadJson, SerializerOptions) ?? new DigestPayload();
}

public sealed class DigestPayload
{
    public IReadOnlyList<DigestItem> NewVideos { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyList<DigestResource> NewResources { get; init; } = Array.Empty<DigestResource>();
    public IReadOnlyList<HighSignalMatch> HighSignalMatches { get; init; } = Array.Empty<HighSignalMatch>();
    public IReadOnlyList<DigestItem> FailedItems { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyList<DigestItem> SkippedItems { get; init; } = Array.Empty<DigestItem>();
    public IReadOnlyList<ActiveDeferment> ActiveDeferments { get; init; } = Array.Empty<ActiveDeferment>();
    public bool HighSignalEvaluationSkipped { get; init; }
    public double HighSignalThresholdPercent { get; init; }
    public bool IsBackfillRun { get; init; }
}

public sealed class DigestItem
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public sealed class DigestResource
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ResourceType { get; init; } = "resource";
    public string? Url { get; init; }
}

public sealed class HighSignalMatch
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public double SimilarityPercent { get; init; }
}

public sealed class ActiveDeferment
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
