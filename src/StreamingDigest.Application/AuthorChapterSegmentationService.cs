using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Builds the initial author-chapter <see cref="SegmentGeneration"/> for a video whose
/// yt-dlp metadata already contains chapter data. Author-chapter generations are
/// auto-activated on creation; they do not require user approval.
/// </summary>
public sealed class AuthorChapterSegmentationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates an author-chapter <see cref="SegmentGeneration"/> from a video's stored
    /// <c>chapters_json</c>. Returns <c>null</c> when the video has no chapter data.
    /// </summary>
    /// <param name="video">The video to segment.</param>
    /// <param name="generationVersion">
    /// The version number for this generation. Defaults to <c>1</c> for the initial
    /// ingestion pass; callers that re-create author-chapter generations should increment.
    /// </param>
    /// <param name="now">
    /// The wall-clock instant to stamp on <c>ActivatedAt</c>. Defaults to
    /// <see cref="DateTimeOffset.UtcNow"/>; injectable for deterministic tests.
    /// </param>
    public SegmentGeneration? CreateFromChaptersJson(
        Video video,
        int generationVersion = 1,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(video);

        var chapters = ParseChapters(video.ChaptersJson);
        if (chapters is not { Count: > 0 })
        {
            return null;
        }

        var activatedAt = now ?? DateTimeOffset.UtcNow;

        var generation = new SegmentGeneration
        {
            VideoId = video.Id,
            SourceType = SegmentSourceTypes.AuthorChapter,
            GenerationVersion = generationVersion,
            IsActive = true,
            RequiresUserApproval = false,
            Status = "active",
            ActivatedAt = activatedAt
        };

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var startSeconds = (decimal)(chapter.StartTimeSeconds ?? 0.0);
            decimal? endSeconds = i < chapters.Count - 1
                ? (decimal)chapters[i + 1].StartTimeSeconds!.Value
                : video.DurationSeconds.HasValue
                    ? (decimal)video.DurationSeconds.Value
                    : (chapter.EndTimeSeconds.HasValue ? (decimal)chapter.EndTimeSeconds.Value : null);

            generation.Segments.Add(new Segment
            {
                VideoId = video.Id,
                SegmentGenerationId = generation.Id,
                SourceType = SegmentSourceTypes.AuthorChapter,
                Sequence = i + 1,
                StartSeconds = startSeconds,
                EndSeconds = endSeconds,
                TitleOriginal = string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {i + 1}" : chapter.Title.Trim()
            });
        }

        return generation;
    }

    private static List<ChapterEntry>? ParseChapters(string? chaptersJson)
    {
        if (string.IsNullOrWhiteSpace(chaptersJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChapterEntry>>(chaptersJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ChapterEntry
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("start_time")]
        public double? StartTimeSeconds { get; set; }

        [JsonPropertyName("end_time")]
        public double? EndTimeSeconds { get; set; }
    }
}
