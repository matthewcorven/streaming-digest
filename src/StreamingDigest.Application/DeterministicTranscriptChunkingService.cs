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
}
