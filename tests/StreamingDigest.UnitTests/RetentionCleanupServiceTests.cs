using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class RetentionCleanupServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StreamingDigestDbContext _context;
    private readonly RetentionCleanupService _service;

    public RetentionCleanupServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _context.Database.EnsureCreated();
        _service = new RetentionCleanupService(_context, new TestLogger<RetentionCleanupService>());
    }

    [Fact]
    public async Task DeleteExpiredDomainEventsAsync_RemovesOnlyExpiredDetailedEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var oldRunId = Guid.NewGuid();

        var oldEvent = new DomainEvent
        {
            EventType = DomainEventTypeCatalog.ScrapeFailed,
            Severity = "error",
            Message = "old detailed event",
            IngestionRunId = oldRunId
        };

        var freshEvent = new DomainEvent
        {
            EventType = DomainEventTypeCatalog.ScrapeFailed,
            Severity = "error",
            Message = "fresh detailed event"
        };

        _context.DomainEvents.AddRange(
            oldEvent,
            freshEvent);

        _context.Digests.Add(new Digest(oldRunId, "scheduled")
        {
            PayloadJson = "{\"summary\":\"keep\"}",
            CreatedAt = now.AddDays(-31),
            UpdatedAt = now.AddDays(-31)
        });

        await _context.SaveChangesAsync();

        oldEvent.CreatedAt = now.AddDays(-31);
        freshEvent.CreatedAt = now.AddDays(-5);
        await _context.SaveChangesAsync();

        var deletedCount = await _service.DeleteExpiredDomainEventsAsync(30, now);

        Assert.Equal(1, deletedCount);
        Assert.Single(_context.DomainEvents);
        Assert.Equal("fresh detailed event", (await _context.DomainEvents.SingleAsync()).Message);
        Assert.Single(_context.Digests);
    }

    [Fact]
    public async Task PurgeOwnedArtifactsAsync_DeletesFilesAndArtifactRows()
    {
        var ownerId = Guid.NewGuid();
        var tempDirectory = Directory.CreateTempSubdirectory("streaming-digest-media-purge");

        try
        {
            var screenshotPath = Path.Combine(tempDirectory.FullName, "segment.webp");
            var debugCapturePath = Path.Combine(tempDirectory.FullName, "debug.html");
            await File.WriteAllTextAsync(screenshotPath, "image");
            await File.WriteAllTextAsync(debugCapturePath, "<html></html>");

            _context.MediaArtifacts.AddRange(
                new MediaArtifact
                {
                    OwnerType = MediaArtifactOwnerTypes.Video,
                    OwnerId = ownerId,
                    ArtifactKind = MediaArtifactKinds.Screenshot,
                    FilePath = screenshotPath
                },
                new MediaArtifact
                {
                    OwnerType = MediaArtifactOwnerTypes.Video,
                    OwnerId = ownerId,
                    ArtifactKind = MediaArtifactKinds.RawHtmlDebugCapture,
                    FilePath = debugCapturePath
                });

            await _context.SaveChangesAsync();

            var result = await _service.PurgeOwnedArtifactsAsync(MediaArtifactOwnerTypes.Video, [ownerId]);

            Assert.Equal(2, result.DeletedArtifactRecordCount);
            Assert.Equal(2, result.DeletedFileCount);
            Assert.False(File.Exists(screenshotPath));
            Assert.False(File.Exists(debugCapturePath));
            Assert.Empty(_context.MediaArtifacts);
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
