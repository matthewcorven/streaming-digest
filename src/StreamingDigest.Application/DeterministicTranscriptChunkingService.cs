using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Builds a deterministic-chunk <see cref="SegmentGeneration"/> by grouping a video's
/// transcript cues into fixed-duration time windows. Used as the fallback segmentation
/// strategy when no author chapters are present.
/// </summary>
/// <remarks>
/// The result is auto-activated and requires no user approval. Every cue is linked to
/// its containing segment via <see cref="SegmentTranscriptRange"/> rows stored on the
/// segment, so callers only need to persist the returned generation and its reachable
/// navigation graph.
/// </remarks>
public sealed class DeterministicTranscriptChunkingService
{
    /// <summary>Default time-window width in seconds (2 minutes).</summary>
    public const int DefaultWindowSeconds = 120;

    private readonly HttpClient _httpClient;
    private readonly ILogger<DeterministicTranscriptChunkingService>? _logger;

    public DeterministicTranscriptChunkingService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, null)
    {
    }

    public DeterministicTranscriptChunkingService(
        HttpClient httpClient,
        ILogger<DeterministicTranscriptChunkingService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;

        var configuredBaseAddress = ResolveConfiguredBaseAddress();
        if (_httpClient.BaseAddress is null && configuredBaseAddress is not null)
        {
            _httpClient.BaseAddress = configuredBaseAddress;
        }
    }

    /// <summary>
    /// Creates a deterministic-chunk <see cref="SegmentGeneration"/> from an ordered
    /// list of <see cref="TranscriptCue"/> objects.
    /// Returns <c>null</c> when <paramref name="cues"/> is empty.
    /// </summary>
    /// <param name="video">The video being chunked.</param>
    /// <param name="cues">
    /// The transcript cues to group. Need not be sorted — the service sorts by
    /// <see cref="TranscriptCue.StartSeconds"/> before bucketing.
    /// </param>
    /// <param name="windowSeconds">
    /// Width of each time window in seconds. Defaults to
    /// <see cref="DefaultWindowSeconds"/>; injectable for deterministic tests.
    /// </param>
    /// <param name="generationVersion">
    /// Version number for this generation. Defaults to <c>1</c>; callers that
    /// re-create chunked generations should increment.
    /// </param>
    /// <param name="now">
    /// Wall-clock instant stamped on <c>ActivatedAt</c>. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>; injectable for deterministic tests.
    /// </param>
    public SegmentGeneration? CreateFromTranscriptCues(
        Video video,
        IReadOnlyList<TranscriptCue> cues,
        int windowSeconds = DefaultWindowSeconds,
        int generationVersion = 1,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(cues);
        if (windowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSeconds),
                "Window size must be greater than zero.");
        }

        if (cues.Count == 0)
        {
            return null;
        }

        var activatedAt = now ?? DateTimeOffset.UtcNow;

        var generation = new SegmentGeneration
        {
            VideoId = video.Id,
            SourceType = SegmentSourceTypes.DeterministicChunk,
            GenerationVersion = generationVersion,
            IsActive = true,
            RequiresUserApproval = false,
            Status = "active",
            ActivatedAt = activatedAt
        };

        var sortedCues = cues.OrderBy(c => c.StartSeconds).ToList();
        var buckets = BucketCues(sortedCues, windowSeconds);

        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            var startSeconds = bucket[0].StartSeconds;

            decimal? endSeconds;
            if (i < buckets.Count - 1)
            {
                // End at the start of the next bucket's first cue.
                endSeconds = buckets[i + 1][0].StartSeconds;
            }
            else if (video.DurationSeconds.HasValue)
            {
                endSeconds = (decimal)video.DurationSeconds.Value;
            }
            else
            {
                // Fall back to the last cue's end time, which may itself be null.
                endSeconds = bucket[^1].EndSeconds;
            }

            var segment = new Segment
            {
                VideoId = video.Id,
                SegmentGenerationId = generation.Id,
                SourceType = SegmentSourceTypes.DeterministicChunk,
                Sequence = i + 1,
                StartSeconds = startSeconds,
                EndSeconds = endSeconds,
                TitleOriginal = $"Part {i + 1}"
            };

            foreach (var cue in bucket)
            {
                segment.TranscriptRanges.Add(new SegmentTranscriptRange
                {
                    SegmentId = segment.Id,
                    TranscriptCueId = cue.Id
                });
            }

            generation.Segments.Add(segment);
        }

        TryApplySemanticRefinement(video, sortedCues, generation);

        return generation;
    }

    private static List<List<TranscriptCue>> BucketCues(
        IReadOnlyList<TranscriptCue> sortedCues,
        int windowSeconds)
    {
        var buckets = new List<List<TranscriptCue>>();
        var current = new List<TranscriptCue>();
        var bucketStart = sortedCues[0].StartSeconds;

        foreach (var cue in sortedCues)
        {
            if (current.Count > 0 && cue.StartSeconds >= bucketStart + windowSeconds)
            {
                buckets.Add(current);
                current = [];
                bucketStart = cue.StartSeconds;
            }

            current.Add(cue);
        }

        if (current.Count > 0)
        {
            buckets.Add(current);
        }

        return buckets;
    }

    private void TryApplySemanticRefinement(
        Video video,
        IReadOnlyList<TranscriptCue> cues,
        SegmentGeneration generation)
    {
        if (generation.Segments.Count == 0 || !SemanticRefinementEnabled)
        {
            return;
        }

        try
        {
            var requestPayload = new
            {
                model = ResolveModelName(),
                stream = false,
                format = "json",
                options = new { temperature = 0.0 },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You refine transcript segments into semantically coherent chunks. Return JSON with a top-level 'segments' array. Each item must include 'title', 'startSeconds', and 'endSeconds'. Keep the same number of segments as the input segments."
                    },
                    new
                    {
                        role = "user",
                        content = BuildRefinementPrompt(video, cues, generation)
                    }
                }
            };

            using var response = _httpClient.PostAsJsonAsync("api/chat", requestPayload).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!TryParseRefinementResponse(responseBody, out var refinedSegments))
            {
                return;
            }

            if (refinedSegments.Count != generation.Segments.Count)
            {
                return;
            }

            generation.LlmModel = ResolveModelName();
            generation.LlmPromptVersion = "segment-refinement-v1";

            var refinedSegmentModels = new List<Segment>();
            decimal? previousEnd = null;
            for (var i = 0; i < generation.Segments.Count; i++)
            {
                var baselineSegment = generation.Segments[i];
                var refinement = refinedSegments[i];

                if (!string.IsNullOrWhiteSpace(refinement.Title))
                {
                    refinement.Title = refinement.Title!.Trim();
                }

                var startSeconds = refinement.StartSeconds ?? baselineSegment.StartSeconds;
                var endSeconds = refinement.EndSeconds ?? baselineSegment.EndSeconds;

                if (previousEnd is not null && startSeconds < previousEnd.Value)
                {
                    startSeconds = previousEnd.Value;
                }

                if (endSeconds is not null && endSeconds < startSeconds)
                {
                    endSeconds = startSeconds;
                }

                var updatedSegment = new Segment
                {
                    VideoId = baselineSegment.VideoId,
                    SegmentGenerationId = baselineSegment.SegmentGenerationId,
                    SourceType = baselineSegment.SourceType,
                    Sequence = baselineSegment.Sequence,
                    StartSeconds = startSeconds,
                    EndSeconds = endSeconds,
                    TitleOriginal = string.IsNullOrWhiteSpace(refinement.Title) ? baselineSegment.TitleOriginal : refinement.Title,
                    LlmModel = generation.LlmModel,
                    LlmPromptVersion = generation.LlmPromptVersion
                };

                foreach (var transcriptRange in baselineSegment.TranscriptRanges)
                {
                    updatedSegment.TranscriptRanges.Add(new SegmentTranscriptRange
                    {
                        SegmentId = updatedSegment.Id,
                        TranscriptCueId = transcriptRange.TranscriptCueId
                    });
                }

                refinedSegmentModels.Add(updatedSegment);
                previousEnd = endSeconds ?? startSeconds;
            }

            generation.Segments.Clear();
            foreach (var segment in refinedSegmentModels)
            {
                generation.Segments.Add(segment);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Semantic refinement failed for transcript chunking generation {GenerationId}.", generation.Id);
        }
    }

    private bool SemanticRefinementEnabled => _httpClient.BaseAddress is not null;

    private static Uri? ResolveConfiguredBaseAddress()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("STREAMINGDIGEST_LLM_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return null;
        }

        return Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredBaseAddress)
            ? configuredBaseAddress
            : null;
    }

    private static string ResolveModelName()
    {
        var configuredModel = Environment.GetEnvironmentVariable("STREAMINGDIGEST_LLM_MODEL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        return string.IsNullOrWhiteSpace(configuredModel) ? "local-llm" : configuredModel;
    }

    private static string BuildRefinementPrompt(
        Video video,
        IReadOnlyList<TranscriptCue> cues,
        SegmentGeneration generation)
    {
        var baselineSegments = generation.Segments
            .Select((segment, index) => $"[{index + 1}] title='{segment.TitleOriginal}' start={segment.StartSeconds} end={segment.EndSeconds}")
            .ToList();

        var cueSummaries = cues.Take(12).Select(cue => $"[{cue.Sequence}] {cue.StartSeconds}s - {cue.TextOriginal}").ToList();
        var additionalContext = cues.Count > 12
            ? $"\n... and {cues.Count - 12} more cues"
            : string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine($"Video title: {video.Title}");
        builder.AppendLine("Revise the deterministic segments to make them more semantically coherent.");
        builder.AppendLine("Preserve the same number of segments as the baseline and keep the segments in chronological order.");
        builder.AppendLine("Baseline segments:");
        foreach (var segment in baselineSegments)
        {
            builder.AppendLine(segment);
        }

        builder.AppendLine("Transcript cues:");
        foreach (var cueSummary in cueSummaries)
        {
            builder.AppendLine(cueSummary);
        }

        builder.AppendLine(additionalContext);

        return builder.ToString();
    }

    private static bool TryParseRefinementResponse(
        string responseBody,
        out List<SegmentRefinement> refinedSegments)
    {
        refinedSegments = [];
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            var normalizedPayload = ExtractJsonPayload(responseBody);
            if (string.IsNullOrWhiteSpace(normalizedPayload))
            {
                return false;
            }

            using var document = JsonDocument.Parse(normalizedPayload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("segments", out var segmentsElement) && segmentsElement.ValueKind == JsonValueKind.Array)
            {
                return TryParseSegments(segmentsElement, out refinedSegments);
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.Object)
            {
                if (messageElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                {
                    return TryParseRefinementResponse(contentElement.GetString() ?? string.Empty, out refinedSegments);
                }
            }

            if (document.RootElement.TryGetProperty("content", out var nestedContentElement) && nestedContentElement.ValueKind == JsonValueKind.String)
            {
                return TryParseRefinementResponse(nestedContentElement.GetString() ?? string.Empty, out refinedSegments);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseSegments(JsonElement segmentsElement, out List<SegmentRefinement> refinedSegments)
    {
        refinedSegments = [];
        foreach (var segmentElement in segmentsElement.EnumerateArray())
        {
            if (!TryReadRefinement(segmentElement, out var refinement))
            {
                return false;
            }

            refinedSegments.Add(refinement);
        }

        return refinedSegments.Count > 0;
    }

    private static bool TryReadRefinement(JsonElement segmentElement, out SegmentRefinement refinement)
    {
        refinement = new SegmentRefinement();
        if (segmentElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = segmentElement.EnumerateObject().ToDictionary(property => property.Name.ToLowerInvariant(), property => property.Value);
        var titleValue = TryReadString(properties, "title") ?? TryReadString(properties, "segment_title") ?? TryReadString(properties, "name");
        var startSecondsValue = TryReadDecimal(properties, "startseconds") ?? TryReadDecimal(properties, "start_seconds") ?? TryReadDecimal(properties, "start");
        var endSecondsValue = TryReadDecimal(properties, "endseconds") ?? TryReadDecimal(properties, "end_seconds") ?? TryReadDecimal(properties, "end");

        refinement.Title = titleValue;
        refinement.StartSeconds = startSecondsValue;
        refinement.EndSeconds = endSecondsValue;

        return true;
    }

    private static string ExtractJsonPayload(string responseBody)
    {
        var trimmed = responseBody.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceEnd = trimmed.IndexOf("\n", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                trimmed = trimmed[(fenceEnd + 1)..];
            }

            var closingFence = trimmed.IndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string? TryReadString(IReadOnlyDictionary<string, JsonElement> properties, string propertyName)
    {
        if (properties.TryGetValue(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static decimal? TryReadDecimal(IReadOnlyDictionary<string, JsonElement> properties, string propertyName)
    {
        if (!properties.TryGetValue(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            _ => null
        };
    }

    private sealed class SegmentRefinement
    {
        public string? Title { get; set; }
        public decimal? StartSeconds { get; set; }
        public decimal? EndSeconds { get; set; }
    }
}
