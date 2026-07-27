using System.Text.Json;
using StreamingDigest.Application;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class AuthorChapterSegmentationServiceTests
{
    private static readonly AuthorChapterSegmentationService Service = new();

    private static string ChaptersJson(params (string Title, double Start, double End)[] chapters)
    {
        var entries = chapters.Select(c => new { title = c.Title, start_time = c.Start, end_time = c.End });
        return JsonSerializer.Serialize(entries);
    }

    private static Video VideoWith(string? chaptersJson, int? durationSeconds = null) => new(Guid.NewGuid(), "Test Video")
    {
        ChaptersJson = chaptersJson,
        DurationSeconds = durationSeconds
    };

    [Fact]
    public void CreateFromChaptersJson_ReturnsNull_WhenChaptersJsonIsNull()
    {
        var video = VideoWith(null, durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video);

        Assert.Null(result);
    }

    [Fact]
    public void CreateFromChaptersJson_ReturnsNull_WhenChaptersJsonIsEmpty()
    {
        var video = VideoWith("[]", durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video);

        Assert.Null(result);
    }

    [Fact]
    public void CreateFromChaptersJson_ReturnsNull_WhenChaptersJsonIsMalformed()
    {
        var video = VideoWith("not-valid-json", durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video);

        Assert.Null(result);
    }

    [Fact]
    public void CreateFromChaptersJson_IsAutoActivated()
    {
        var video = VideoWith(ChaptersJson(("Intro", 0, 60)), durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.True(result.IsActive);
        Assert.False(result.RequiresUserApproval);
        Assert.Equal("active", result.Status);
        Assert.NotNull(result.ActivatedAt);
    }

    [Fact]
    public void CreateFromChaptersJson_SetsCorrectSourceTypeAndVideoId()
    {
        var video = VideoWith(ChaptersJson(("Intro", 0, 60)), durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Equal(SegmentSourceTypes.AuthorChapter, result.SourceType);
        Assert.Equal(video.Id, result.VideoId);
    }

    [Fact]
    public void CreateFromChaptersJson_SingleChapter_UsesVideoDurationAsEndSeconds()
    {
        var video = VideoWith(ChaptersJson(("Full video", 0, 300)), durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Single(result.Segments);
        var segment = result.Segments[0];
        Assert.Equal(0m, segment.StartSeconds);
        Assert.Equal(300m, segment.EndSeconds);
    }

    [Fact]
    public void CreateFromChaptersJson_SingleChapter_FallsBackToChapterEndTimeWhenNoDuration()
    {
        var video = VideoWith(ChaptersJson(("Full video", 0, 250)));

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Single(result.Segments);
        Assert.Equal(250m, result.Segments[0].EndSeconds);
    }

    [Fact]
    public void CreateFromChaptersJson_MultipleChapters_FillsEndSecondsFromNextChapterStart()
    {
        var chaptersJson = ChaptersJson(
            ("Intro", 0, 60),
            ("Main", 60, 180),
            ("Outro", 180, 300));
        var video = VideoWith(chaptersJson, durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Equal(3, result.Segments.Count);
        Assert.Equal(0m, result.Segments[0].StartSeconds);
        Assert.Equal(60m, result.Segments[0].EndSeconds);   // next chapter start
        Assert.Equal(60m, result.Segments[1].StartSeconds);
        Assert.Equal(180m, result.Segments[1].EndSeconds);  // next chapter start
        Assert.Equal(180m, result.Segments[2].StartSeconds);
        Assert.Equal(300m, result.Segments[2].EndSeconds);  // video duration
    }

    [Fact]
    public void CreateFromChaptersJson_SequenceIsOneIndexed()
    {
        var chaptersJson = ChaptersJson(("A", 0, 60), ("B", 60, 120));
        var video = VideoWith(chaptersJson, durationSeconds: 120);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Equal(1, result.Segments[0].Sequence);
        Assert.Equal(2, result.Segments[1].Sequence);
    }

    [Fact]
    public void CreateFromChaptersJson_SetsSegmentVideoIdAndSourceType()
    {
        var chaptersJson = ChaptersJson(("Intro", 0, 60));
        var video = VideoWith(chaptersJson, durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video)!;

        var segment = result.Segments[0];
        Assert.Equal(video.Id, segment.VideoId);
        Assert.Equal(SegmentSourceTypes.AuthorChapter, segment.SourceType);
        Assert.Equal(result.Id, segment.SegmentGenerationId);
    }

    [Fact]
    public void CreateFromChaptersJson_TrimsAndPreservesChapterTitles()
    {
        var chaptersJson = ChaptersJson(("  Introduction  ", 0, 60), ("", 60, 120));
        var video = VideoWith(chaptersJson, durationSeconds: 120);

        var result = Service.CreateFromChaptersJson(video)!;

        Assert.Equal("Introduction", result.Segments[0].TitleOriginal);
        Assert.Equal("Chapter 2", result.Segments[1].TitleOriginal);
    }

    [Fact]
    public void CreateFromChaptersJson_UsesSuppliedGenerationVersion()
    {
        var video = VideoWith(ChaptersJson(("Intro", 0, 60)), durationSeconds: 300);

        var result = Service.CreateFromChaptersJson(video, generationVersion: 3)!;

        Assert.Equal(3, result.GenerationVersion);
    }

    [Fact]
    public void CreateFromChaptersJson_UsesSuppliedNowTimestamp()
    {
        var video = VideoWith(ChaptersJson(("Intro", 0, 60)), durationSeconds: 300);
        var fixedNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var result = Service.CreateFromChaptersJson(video, now: fixedNow)!;

        Assert.Equal(fixedNow, result.ActivatedAt);
    }
}
