using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.AudioToText;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using Microsoft.Extensions.Logging.Abstractions;

namespace StreamingDigest.UnitTests;

/// <summary>
/// Tests for the audio-to-text provider abstraction (Task 6.2):
/// <list type="bullet">
///   <item><see cref="StubAudioToTextProvider"/> contract</item>
///   <item><see cref="TranscriptIngestionService.GetSourceTypePreference"/> preference order</item>
///   <item>Cutover event emission when a higher-preference source replaces an active transcript</item>
/// </list>
/// </summary>
public sealed class AudioToTextProviderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StreamingDigestDbContext _context;

    public AudioToTextProviderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _context.Database.EnsureCreated();
    }

    // ── StubAudioToTextProvider ───────────────────────────────────────────────

    [Fact]
    public async Task StubAudioToTextProvider_returns_empty_transcription_and_preserves_language_hint()
    {
        var provider = new StubAudioToTextProvider(NullLogger<StubAudioToTextProvider>.Instance);
        var request = new AudioTranscriptionRequest("/tmp/audio.wav", LanguageHint: "en");

        var result = await provider.TranscribeAsync(request, CancellationToken.None);

        Assert.Equal("stub", result.Engine);
        Assert.Equal("stub", result.Model);
        Assert.Equal("en", result.Language);
        Assert.Equal(string.Empty, result.FullText);
        Assert.Empty(result.Cues);
        Assert.Null(result.DurationSeconds);
    }

    [Fact]
    public async Task StubAudioToTextProvider_returns_null_language_when_no_hint_supplied()
    {
        var provider = new StubAudioToTextProvider(NullLogger<StubAudioToTextProvider>.Instance);
        var request = new AudioTranscriptionRequest("/tmp/audio.wav");

        var result = await provider.TranscribeAsync(request, CancellationToken.None);

        Assert.Null(result.Language);
    }

    // ── GetSourceTypePreference preference order ──────────────────────────────

    [Theory]
    [InlineData(VideoTranscriptSourceTypes.YouTubeCaption, 3)]
    [InlineData(VideoTranscriptSourceTypes.LocalWhisper, 2)]
    [InlineData(VideoTranscriptSourceTypes.YouTubeAutoCaption, 1)]
    [InlineData("unknown_source", 0)]
    [InlineData(null, 0)]
    public void GetSourceTypePreference_returns_correct_rank(string? sourceType, int expectedRank)
    {
        Assert.Equal(expectedRank, TranscriptIngestionService.GetSourceTypePreference(sourceType));
    }

    [Fact]
    public void GetSourceTypePreference_youtube_caption_outranks_all_others()
    {
        var highest = TranscriptIngestionService.GetSourceTypePreference(VideoTranscriptSourceTypes.YouTubeCaption);
        Assert.True(highest > TranscriptIngestionService.GetSourceTypePreference(VideoTranscriptSourceTypes.LocalWhisper));
        Assert.True(highest > TranscriptIngestionService.GetSourceTypePreference(VideoTranscriptSourceTypes.YouTubeAutoCaption));
    }

    [Fact]
    public void GetSourceTypePreference_local_whisper_outranks_auto_caption()
    {
        var whisper = TranscriptIngestionService.GetSourceTypePreference(VideoTranscriptSourceTypes.LocalWhisper);
        var auto = TranscriptIngestionService.GetSourceTypePreference(VideoTranscriptSourceTypes.YouTubeAutoCaption);
        Assert.True(whisper > auto);
    }

    // ── Cutover event emission ────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_does_not_emit_cutover_event_for_first_transcript()
    {
        var video = await SeedVideoAsync("video-first");
        var service = CreateServiceWithManualCaption();

        await service.IngestAsync(video.Id, CancellationToken.None);

        var events = _context.DomainEvents.ToList();
        Assert.DoesNotContain(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverCompleted);
    }

    [Fact]
    public async Task IngestAsync_emits_cutover_event_when_youtube_caption_replaces_auto_caption()
    {
        var video = await SeedVideoAsync("video-cutover-auto");
        await SeedActiveTranscriptAsync(video.Id, VideoTranscriptSourceTypes.YouTubeAutoCaption);

        var service = CreateServiceWithManualCaption();
        await service.IngestAsync(video.Id, CancellationToken.None);

        var events = _context.DomainEvents.ToList();
        Assert.Contains(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverCompleted);
        Assert.DoesNotContain(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverOverrideInert);
    }

    [Fact]
    public async Task IngestAsync_emits_override_inert_event_when_replaced_transcript_has_cue_overrides()
    {
        var video = await SeedVideoAsync("video-cutover-overrides");
        var existingTranscript = await SeedActiveTranscriptAsync(video.Id, VideoTranscriptSourceTypes.YouTubeAutoCaption);
        await SeedCueWithOverrideAsync(existingTranscript.Id);

        var service = CreateServiceWithManualCaption();
        await service.IngestAsync(video.Id, CancellationToken.None);

        var events = _context.DomainEvents.ToList();
        Assert.Contains(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverCompleted);
        Assert.Contains(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverOverrideInert);

        var cutoverEvent = events.Single(e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverCompleted);
        Assert.Contains("1 cue override(s)", cutoverEvent.Message);
    }

    [Fact]
    public async Task IngestAsync_does_not_emit_cutover_when_same_preference_source_replaces_existing()
    {
        // Re-ingesting youtube_caption → youtube_caption: same rank, not a cutover.
        var video = await SeedVideoAsync("video-same-preference");
        await SeedActiveTranscriptAsync(video.Id, VideoTranscriptSourceTypes.YouTubeCaption);

        var service = CreateServiceWithManualCaption();
        await service.IngestAsync(video.Id, CancellationToken.None);

        var events = _context.DomainEvents.ToList();
        Assert.DoesNotContain(events, e => e.EventType == DomainEventTypeCatalog.TranscriptCutoverCompleted);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private TranscriptIngestionService CreateServiceWithManualCaption()
    {
        var client = new StubCaptionClient(
            [new CaptionTrackInfo("en", false, "en-manual")],
            new Dictionary<string, CaptionFetchResult>
            {
                ["en-manual"] = new CaptionFetchResult("en", false, "English transcript",
                    [new CaptionCueDto(1, 0m, 2m, "Hello")])
            });
        return new TranscriptIngestionService(_context, client);
    }

    private async Task<Video> SeedVideoAsync(string platformVideoId)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"channel-{platformVideoId}",
            NameOriginal = "Test Channel",
            ProfileUrl = "https://www.youtube.com/@testchannel",
            SourceUrl = "https://www.youtube.com/channel/testchannel"
        };

        var video = new Video(Guid.NewGuid(), "Test Video")
        {
            ChannelId = channel.Id,
            PlatformVideoId = platformVideoId,
            YoutubeVideoId = platformVideoId,
            PlatformVideoUrl = $"https://www.youtube.com/watch?v={platformVideoId}",
            VideoUrl = $"https://www.youtube.com/watch?v={platformVideoId}",
            AuthorOriginal = "Test Author"
        };

        _context.Channels.Add(channel);
        _context.Videos.Add(video);
        await _context.SaveChangesAsync();
        return video;
    }

    private async Task<VideoTranscript> SeedActiveTranscriptAsync(Guid videoId, string sourceType)
    {
        var transcript = new VideoTranscript
        {
            VideoId = videoId,
            SourceType = sourceType,
            FullTextOriginal = "Previous transcript",
            IsActive = true
        };

        _context.VideoTranscripts.Add(transcript);
        await _context.SaveChangesAsync();
        return transcript;
    }

    private async Task SeedCueWithOverrideAsync(Guid transcriptId)
    {
        var cue = new TranscriptCue
        {
            TranscriptId = transcriptId,
            Sequence = 1,
            StartSeconds = 0m,
            EndSeconds = 2m,
            TextOriginal = "Original text",
            TextOverride = "User-edited text"
        };

        _context.TranscriptCues.Add(cue);
        await _context.SaveChangesAsync();
    }

    private sealed class StubCaptionClient(
        IReadOnlyList<CaptionTrackInfo> availableTracks,
        IReadOnlyDictionary<string, CaptionFetchResult> fetchResults) : IYouTubeCaptionClient
    {
        public Task<IReadOnlyList<CaptionTrackInfo>> GetAvailableTracksAsync(string videoId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CaptionTrackInfo>>(availableTracks);

        public Task<CaptionFetchResult> FetchTrackAsync(string videoId, string trackCode, CancellationToken ct)
            => Task.FromResult(fetchResults[trackCode]);
    }
}
