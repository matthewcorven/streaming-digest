using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class RepositoryMediaPurgeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StreamingDigestDbContext _context;
    private readonly RetentionCleanupService _cleanupService;
    private readonly VideoRepository _videoRepository;
    private readonly ChannelRepository _channelRepository;

    public RepositoryMediaPurgeTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _context.Database.EnsureCreated();

        _cleanupService = new RetentionCleanupService(_context, new TestLogger<RetentionCleanupService>());
        _videoRepository = new VideoRepository(_context, _cleanupService);
        _channelRepository = new ChannelRepository(_context, _cleanupService);
    }

    [Fact]
    public async Task VideoDeleteAsync_WithMediaPurge_RemovesTrackedFiles()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-video-purge",
            NameOriginal = "Channel",
            ProfileUrl = "https://youtube.com/@channel",
            SourceUrl = "https://youtube.com/channel/UC-video-purge"
        };

        var video = new Video(Guid.NewGuid(), "Video")
        {
            ChannelId = channel.Id,
            PlatformVideoUrl = "https://youtube.com/watch?v=video-purge",
            PlatformVideoId = "video-purge",
            YoutubeVideoId = "video-purge",
            AuthorOriginal = "Author",
            VideoUrl = "https://youtube.com/watch?v=video-purge"
        };

        var tempDirectory = Directory.CreateTempSubdirectory("streaming-digest-video-delete");

        try
        {
            var screenshotPath = Path.Combine(tempDirectory.FullName, "video.webp");
            await File.WriteAllTextAsync(screenshotPath, "image");

            _context.Channels.Add(channel);
            _context.Videos.Add(video);
            _context.MediaArtifacts.Add(new MediaArtifact
            {
                OwnerType = MediaArtifactOwnerTypes.Video,
                OwnerId = video.Id,
                ArtifactKind = MediaArtifactKinds.Screenshot,
                FilePath = screenshotPath
            });
            await _context.SaveChangesAsync();

            await _videoRepository.DeleteAsync(video.Id, purgeMedia: true);

            Assert.Empty(_context.Videos);
            Assert.Empty(_context.MediaArtifacts);
            Assert.False(File.Exists(screenshotPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ChannelDeleteAsync_WithMediaPurge_RemovesChildVideoArtifacts()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-channel-purge",
            NameOriginal = "Channel",
            ProfileUrl = "https://youtube.com/@channel-purge",
            SourceUrl = "https://youtube.com/channel/UC-channel-purge"
        };

        var video = new Video(Guid.NewGuid(), "Video")
        {
            ChannelId = channel.Id,
            PlatformVideoUrl = "https://youtube.com/watch?v=channel-purge",
            PlatformVideoId = "channel-purge",
            YoutubeVideoId = "channel-purge",
            AuthorOriginal = "Author",
            VideoUrl = "https://youtube.com/watch?v=channel-purge"
        };

        var tempDirectory = Directory.CreateTempSubdirectory("streaming-digest-channel-delete");

        try
        {
            var screenshotPath = Path.Combine(tempDirectory.FullName, "child.webp");
            var debugCapturePath = Path.Combine(tempDirectory.FullName, "channel-debug.html");
            await File.WriteAllTextAsync(screenshotPath, "image");
            await File.WriteAllTextAsync(debugCapturePath, "<html></html>");

            _context.Channels.Add(channel);
            _context.Videos.Add(video);
            _context.MediaArtifacts.AddRange(
                new MediaArtifact
                {
                    OwnerType = MediaArtifactOwnerTypes.Video,
                    OwnerId = video.Id,
                    ArtifactKind = MediaArtifactKinds.Screenshot,
                    FilePath = screenshotPath
                },
                new MediaArtifact
                {
                    OwnerType = MediaArtifactOwnerTypes.Channel,
                    OwnerId = channel.Id,
                    ArtifactKind = MediaArtifactKinds.RawHtmlDebugCapture,
                    FilePath = debugCapturePath
                });
            await _context.SaveChangesAsync();

            await _channelRepository.DeleteAsync(channel.Id, purgeMedia: true);

            Assert.Empty(_context.Channels);
            Assert.Empty(_context.Videos);
            Assert.Empty(_context.MediaArtifacts);
            Assert.False(File.Exists(screenshotPath));
            Assert.False(File.Exists(debugCapturePath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
