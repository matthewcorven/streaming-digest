using System.Net;
using StreamingDigest.Application;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class DeterministicTranscriptChunkingServiceTests
{
    private static readonly DeterministicTranscriptChunkingService Service = new();

    private static Video VideoWith(int? durationSeconds = null) => new(Guid.NewGuid(), "Test Video")
    {
        DurationSeconds = durationSeconds
    };

    private static TranscriptCue Cue(int sequence, double startSeconds, double? endSeconds = null, string? text = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TranscriptId = Guid.NewGuid(),
            Sequence = sequence,
            StartSeconds = (decimal)startSeconds,
            EndSeconds = endSeconds.HasValue ? (decimal)endSeconds.Value : null,
            TextOriginal = text ?? $"Cue {sequence}"
        };

    [Fact]
    public void CreateFromTranscriptCues_ReturnsNull_WhenCuesIsEmpty()
    {
        var video = VideoWith(durationSeconds: 300);

        var result = Service.CreateFromTranscriptCues(video, []);

        Assert.Null(result);
    }

    [Fact]
    public void CreateFromTranscriptCues_IsAutoActivated()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 30) };

        var result = Service.CreateFromTranscriptCues(video, cues)!;

        Assert.True(result.IsActive);
        Assert.False(result.RequiresUserApproval);
        Assert.Equal("active", result.Status);
        Assert.NotNull(result.ActivatedAt);
    }

    [Fact]
    public void CreateFromTranscriptCues_SetsCorrectSourceTypeAndVideoId()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 30) };

        var result = Service.CreateFromTranscriptCues(video, cues)!;

        Assert.Equal(SegmentSourceTypes.DeterministicChunk, result.SourceType);
        Assert.Equal(video.Id, result.VideoId);
    }

    [Fact]
    public void CreateFromTranscriptCues_AllCuesInOneWindow_ProducesSingleSegment()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 30), Cue(2, 30, 60), Cue(3, 60, 90) };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Single(result.Segments);
    }

    [Fact]
    public void CreateFromTranscriptCues_SingleSegment_UsesVideoDurationAsEndSeconds()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal(0m, result.Segments[0].StartSeconds);
        Assert.Equal(300m, result.Segments[0].EndSeconds);
    }

    [Fact]
    public void CreateFromTranscriptCues_SingleSegment_FallsBackToLastCueEndSeconds_WhenNoDuration()
    {
        var video = VideoWith();
        var cues = new[] { Cue(1, 0, 90) };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal(90m, result.Segments[0].EndSeconds);
    }

    [Fact]
    public void CreateFromTranscriptCues_SingleSegment_EndSecondsIsNull_WhenNoDurationAndNoCueEnd()
    {
        var video = VideoWith();
        var cues = new[] { Cue(1, 0) };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Null(result.Segments[0].EndSeconds);
    }

    [Fact]
    public void CreateFromTranscriptCues_MultipleWindows_ProducesCorrectSegmentCount()
    {
        var video = VideoWith(durationSeconds: 300);
        // 0–90s in window 0, 120–210s in window 1, 240s in window 2
        var cues = new[]
        {
            Cue(1, 0, 30), Cue(2, 30, 60), Cue(3, 60, 90),
            Cue(4, 120, 150), Cue(5, 150, 210),
            Cue(6, 240, 270)
        };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal(3, result.Segments.Count);
    }

    [Fact]
    public void CreateFromTranscriptCues_MultipleWindows_SetsCorrectBoundaries()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[]
        {
            Cue(1, 0, 60), Cue(2, 60, 120),
            Cue(3, 120, 180), Cue(4, 180, 240)
        };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(0m, result.Segments[0].StartSeconds);
        Assert.Equal(120m, result.Segments[0].EndSeconds); // next bucket start
        Assert.Equal(120m, result.Segments[1].StartSeconds);
        Assert.Equal(300m, result.Segments[1].EndSeconds); // video duration
    }

    [Fact]
    public void CreateFromTranscriptCues_SequenceIsOneIndexed()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[]
        {
            Cue(1, 0, 60), Cue(2, 120, 180)
        };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 100)!;

        Assert.Equal(1, result.Segments[0].Sequence);
        Assert.Equal(2, result.Segments[1].Sequence);
    }

    [Fact]
    public void CreateFromTranscriptCues_TitleIsPartN()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[]
        {
            Cue(1, 0, 60), Cue(2, 120, 180)
        };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 100)!;

        Assert.Equal("Part 1", result.Segments[0].TitleOriginal);
        Assert.Equal("Part 2", result.Segments[1].TitleOriginal);
    }

    [Fact]
    public void CreateFromTranscriptCues_SetsSegmentVideoIdAndSourceType()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };

        var result = Service.CreateFromTranscriptCues(video, cues)!;

        var segment = result.Segments[0];
        Assert.Equal(video.Id, segment.VideoId);
        Assert.Equal(SegmentSourceTypes.DeterministicChunk, segment.SourceType);
        Assert.Equal(result.Id, segment.SegmentGenerationId);
    }

    [Fact]
    public void CreateFromTranscriptCues_TranscriptRangesContainAllCuesForSegment()
    {
        var video = VideoWith(durationSeconds: 300);
        var cue1 = Cue(1, 0, 30);
        var cue2 = Cue(2, 30, 60);
        var cue3 = Cue(3, 60, 90);
        var cues = new[] { cue1, cue2, cue3 };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        var segment = result.Segments[0];
        var rangeIds = segment.TranscriptRanges.Select(r => r.TranscriptCueId).ToHashSet();
        Assert.Contains(cue1.Id, rangeIds);
        Assert.Contains(cue2.Id, rangeIds);
        Assert.Contains(cue3.Id, rangeIds);
        Assert.Equal(3, segment.TranscriptRanges.Count);
    }

    [Fact]
    public void CreateFromTranscriptCues_TranscriptRangesReferenceCorrectSegmentId()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };

        var result = Service.CreateFromTranscriptCues(video, cues)!;

        var segment = result.Segments[0];
        Assert.All(segment.TranscriptRanges, r => Assert.Equal(segment.Id, r.SegmentId));
    }

    [Fact]
    public void CreateFromTranscriptCues_CuesSplitAcrossBuckets_EachBucketHasCorrectRanges()
    {
        var video = VideoWith(durationSeconds: 300);
        var cue1 = Cue(1, 0, 60);
        var cue2 = Cue(2, 60, 120);
        var cue3 = Cue(3, 120, 180);
        var cues = new[] { cue1, cue2, cue3 };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal(2, result.Segments.Count);

        var seg0Ids = result.Segments[0].TranscriptRanges.Select(r => r.TranscriptCueId).ToHashSet();
        Assert.Contains(cue1.Id, seg0Ids);
        Assert.Contains(cue2.Id, seg0Ids);

        var seg1Ids = result.Segments[1].TranscriptRanges.Select(r => r.TranscriptCueId).ToHashSet();
        Assert.Contains(cue3.Id, seg1Ids);
    }

    [Fact]
    public void CreateFromTranscriptCues_UnsortedCues_ProducesSameResultAsSorted()
    {
        var video = VideoWith(durationSeconds: 300);
        var cue1 = Cue(1, 0, 60);
        var cue2 = Cue(2, 60, 120);
        var cue3 = Cue(3, 120, 180);

        var sortedResult = Service.CreateFromTranscriptCues(video, [cue1, cue2, cue3], windowSeconds: 120)!;
        var unsortedResult = Service.CreateFromTranscriptCues(video, [cue3, cue1, cue2], windowSeconds: 120)!;

        Assert.Equal(sortedResult.Segments.Count, unsortedResult.Segments.Count);
        for (var i = 0; i < sortedResult.Segments.Count; i++)
        {
            Assert.Equal(sortedResult.Segments[i].StartSeconds, unsortedResult.Segments[i].StartSeconds);
            Assert.Equal(sortedResult.Segments[i].EndSeconds, unsortedResult.Segments[i].EndSeconds);
        }
    }

    [Fact]
    public void CreateFromTranscriptCues_UsesSuppliedGenerationVersion()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };

        var result = Service.CreateFromTranscriptCues(video, cues, generationVersion: 5)!;

        Assert.Equal(5, result.GenerationVersion);
    }

    [Fact]
    public void CreateFromTranscriptCues_UsesSuppliedNowTimestamp()
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };
        var fixedNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var result = Service.CreateFromTranscriptCues(video, cues, now: fixedNow)!;

        Assert.Equal(fixedNow, result.ActivatedAt);
    }

    [Fact]
    public void CreateFromTranscriptCues_AppliesLlmRefinement_WhenPayloadIsValid()
    {
        var video = VideoWith(durationSeconds: 180);
        var cues = new[] { Cue(1, 0, 60), Cue(2, 60, 120), Cue(3, 120, 180) };
        var handler = new StubHttpMessageHandler("""
        {
          "segments": [
            { "title": "Opening", "startSeconds": 0, "endSeconds": 90 },
            { "title": "Wrap-up", "startSeconds": 90, "endSeconds": 180 }
          ]
        }
        """);
        var mockWrapper = new MockMeaiChatClientWrapper("""
        {
         "segments": [
           { "title": "Opening", "startSeconds": 0, "endSeconds": 90 },
           { "title": "Wrap-up", "startSeconds": 90, "endSeconds": 180 }
         ]
        }
        """);
        var refinementService = new DeterministicTranscriptChunkingService(mockWrapper);

        var result = refinementService.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal("Opening", result.Segments[0].TitleOriginal);
        Assert.Equal(0m, result.Segments[0].StartSeconds);
        Assert.Equal(90m, result.Segments[0].EndSeconds);
        Assert.Equal("Wrap-up", result.Segments[1].TitleOriginal);
        Assert.Equal(90m, result.Segments[1].StartSeconds);
        Assert.Equal(180m, result.Segments[1].EndSeconds);
        Assert.Equal("local-llm", result.LlmModel);
        Assert.Equal("segment-refinement-v1", result.LlmPromptVersion);
    }

    [Fact]
    public void CreateFromTranscriptCues_PreservesDeterministicSegments_WhenLlmPayloadIsInvalid()
    {
        var video = VideoWith(durationSeconds: 180);
        var cues = new[] { Cue(1, 0, 60), Cue(2, 60, 120), Cue(3, 120, 180) };
        var mockWrapper = new MockMeaiChatClientWrapper("not-json");
        var refinementService = new DeterministicTranscriptChunkingService(mockWrapper);

        var result = refinementService.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Equal("Part 1", result.Segments[0].TitleOriginal);
        Assert.Equal(0m, result.Segments[0].StartSeconds);
        Assert.Equal(120m, result.Segments[0].EndSeconds);
        Assert.Equal("Part 2", result.Segments[1].TitleOriginal);
        Assert.Equal(120m, result.Segments[1].StartSeconds);
        Assert.Equal(180m, result.Segments[1].EndSeconds);
        Assert.Null(result.LlmModel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CreateFromTranscriptCues_Throws_WhenWindowSecondsIsNotPositive(int invalidWindow)
    {
        var video = VideoWith(durationSeconds: 300);
        var cues = new[] { Cue(1, 0, 60) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Service.CreateFromTranscriptCues(video, cues, windowSeconds: invalidWindow));
    }

    [Fact]
    public void CreateFromTranscriptCues_Throws_WhenVideoIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Service.CreateFromTranscriptCues(null!, []));
    }

    [Fact]
    public void CreateFromTranscriptCues_Throws_WhenCuesIsNull()
    {
        var video = VideoWith(durationSeconds: 300);

        Assert.Throws<ArgumentNullException>(() =>
            Service.CreateFromTranscriptCues(video, null!));
    }

    [Fact]
    public void CreateFromTranscriptCues_SingleCue_ProducesOneSegment()
    {
        var video = VideoWith(durationSeconds: 60);
        var cues = new[] { Cue(1, 0, 60) };

        var result = Service.CreateFromTranscriptCues(video, cues, windowSeconds: 120)!;

        Assert.Single(result.Segments);
        Assert.Single(result.Segments[0].TranscriptRanges);
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }

    private sealed class MockMeaiChatClientWrapper : MeaiChatClientWrapper
    {
        private readonly string _responseContent;

        public MockMeaiChatClientWrapper(string responseContent)
            : base(new HttpClient())
        {
            _responseContent = responseContent;
        }

        public override Task<string?> SendChatAsync(
            string systemPrompt,
            string userPrompt,
            string modelName,
            double temperature = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(_responseContent);
        }
    }
}
