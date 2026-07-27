using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class TranscriptIngestionFallbackTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StreamingDigestDbContext _context;

    public TranscriptIngestionFallbackTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task IngestAsync_falls_back_to_audio_transcription_and_manages_temp_media_lifecycle()
    {
        var video = await SeedVideoAsync("fallback-video");
        var tempManager = new RecordingTemporaryMediaManager();
        var provider = new RecordingAudioToTextProvider(
            new AudioTranscriptionResult("whisper.cpp", "base.en", "en", 4.2m, "Hello from whisper", [new AudioTranscriptionCueDto(0m, 1m, "Hello")]),
            new List<string>());
        var service = new TranscriptIngestionService(
            _context,
            new StubCaptionClient([]),
            provider,
            tempManager,
            (_, _) => Task.FromResult<string?>("/tmp/source-media.mp4"));

        var result = await service.IngestAsync(video.Id, CancellationToken.None);

        var transcript = await _context.VideoTranscripts.SingleAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(VideoTranscriptSourceTypes.LocalWhisper, result.SourceType);
        Assert.Equal("Hello from whisper", transcript.FullTextOriginal);
        Assert.Single(transcript.Cues);
        Assert.Equal("/tmp/temp-media.mp4", provider.RequestedFilePaths.Single());
        Assert.Equal(["/tmp/temp-media.mp4"], tempManager.CreatedPaths);
        Assert.Equal(["/tmp/temp-media.mp4"], tempManager.DeletedPaths);
    }

    [Fact]
    public async Task IngestAsync_returns_failure_when_audio_fallback_produces_empty_transcript()
    {
        var video = await SeedVideoAsync("fallback-empty");
        var tempManager = new RecordingTemporaryMediaManager();
        var provider = new RecordingAudioToTextProvider(
            new AudioTranscriptionResult("whisper.cpp", "base.en", "en", null, string.Empty, []),
            new List<string>());
        var service = new TranscriptIngestionService(
            _context,
            new StubCaptionClient([]),
            provider,
            tempManager,
            (_, _) => Task.FromResult<string?>("/tmp/source-media.mp4"));

        var result = await service.IngestAsync(video.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("no_captions_available", result.ErrorMessage);
        Assert.Empty(await _context.VideoTranscripts.ToListAsync());
    }

    [Fact]
    public async Task IngestAsync_returns_failure_when_audio_fallback_throws_and_cleans_up_temp_media()
    {
        var video = await SeedVideoAsync("fallback-exception");
        var tempManager = new RecordingTemporaryMediaManager();
        var service = new TranscriptIngestionService(
            _context,
            new StubCaptionClient([]),
            new ThrowingAudioToTextProvider(),
            tempManager,
            (_, _) => Task.FromResult<string?>("/tmp/source-media.mp4"));

        var result = await service.IngestAsync(video.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("audio_transcription_failed", result.ErrorMessage);
        Assert.Empty(await _context.VideoTranscripts.ToListAsync());
        Assert.Equal(["/tmp/temp-media.mp4"], tempManager.CreatedPaths);
        Assert.Equal(["/tmp/temp-media.mp4"], tempManager.DeletedPaths);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
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

    private sealed class StubCaptionClient(IReadOnlyList<CaptionTrackInfo> availableTracks) : IYouTubeCaptionClient
    {
        public Task<IReadOnlyList<CaptionTrackInfo>> GetAvailableTracksAsync(string videoId, CancellationToken ct)
            => Task.FromResult(availableTracks);

        public Task<CaptionFetchResult> FetchTrackAsync(string videoId, string trackCode, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class RecordingAudioToTextProvider(AudioTranscriptionResult result, List<string> requestedFilePaths) : IAudioToTextProvider
    {
        public List<string> RequestedFilePaths { get; } = requestedFilePaths;

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
        {
            RequestedFilePaths.Add(request.FilePath);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingAudioToTextProvider : IAudioToTextProvider
    {
        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
            => throw new InvalidOperationException("transcription unavailable");
    }

    private sealed class RecordingTemporaryMediaManager : ITemporaryMediaManager
    {
        public List<string> CreatedPaths { get; } = [];
        public List<string> DeletedPaths { get; } = [];

        public Task<string> CreateTemporaryMediaAsync(string sourceFilePath, CancellationToken cancellationToken)
        {
            var tempPath = "/tmp/temp-media.mp4";
            CreatedPaths.Add(tempPath);
            return Task.FromResult(tempPath);
        }

        public Task DeleteTemporaryMediaAsync(string? filePath, CancellationToken cancellationToken)
        {
            if (filePath is not null)
            {
                DeletedPaths.Add(filePath);
            }

            return Task.CompletedTask;
        }
    }
}
