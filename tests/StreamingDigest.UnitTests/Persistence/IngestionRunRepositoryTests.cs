using StreamingDigest.Domain;
using Xunit;

namespace StreamingDigest.UnitTests.Persistence;

public sealed class IngestionRunRepositoryMappingTests
{
    [Fact]
    public void IngestionRun_MapsAllPropertiesToAndFromDatabase()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var run = new IngestionRun
        {
            Id = runId,
            OperationId = operationId,
            CorrelationId = "test-correlation-123",
            ScheduleId = "daily-6am",
            RunType = "scheduled",
            TriggeredBy = "system",
            RequestedByUserId = userId,
            Status = "running",
            StartedAt = createdAt,
            CompletedAt = null,
            ChannelsChecked = 5,
            NewVideosFound = 3,
            VideosIngested = 2,
            VideosFailed = 0,
            VideosSkipped = 1,
            TranscriptsFound = 2,
            TranscriptsMissing = 0,
            RepositoriesFound = 4,
            ConfigSnapshotJson = """{"version": 1}""",
            SummaryJson = """{"status": "ok"}""",
            CreatedAt = createdAt
        };

        // Act & Assert
        Assert.Equal(runId, run.Id);
        Assert.Equal(operationId, run.OperationId);
        Assert.Equal("test-correlation-123", run.CorrelationId);
        Assert.Equal("scheduled", run.RunType);
        Assert.Equal("running", run.Status);
    }
}

public sealed class IngestionItemRepositoryMappingTests
{
    [Fact]
    public void IngestionItem_MapsAllPropertiesIncludingPerStageStatus()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var item = new IngestionItem
        {
            Id = itemId,
            IngestionRunId = runId,
            OperationId = operationId,
            ItemType = "video",
            ItemId = Guid.NewGuid(),
            ExternalKey = "yt-123abc",
            IdempotencyKey = "yt-normalized-url",
            Stage = "transcript",
            Status = "completed",
            Attempt = 1,
            RetryCount = 0,
            MaxAttempts = 7,
            IsRetryable = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            TranscriptStatus = "completed",
            SegmentsStatus = "pending",
            ScreenshotsStatus = "pending",
            LinksStatus = "pending",
            ReposStatus = "pending",
            WebsitesStatus = "pending",
            EmbeddingsStatus = "pending"
        };

        // Act & Assert
        Assert.Equal(itemId, item.Id);
        Assert.Equal(runId, item.IngestionRunId);
        Assert.Equal("transcript", item.Stage);
        Assert.Equal("completed", item.TranscriptStatus);
        Assert.Equal("pending", item.SegmentsStatus);
        Assert.Equal("pending", item.EmbeddingsStatus);
    }

    [Fact]
    public void IngestionItem_InitializesPerStageStatusToDefault()
    {
        // Arrange & Act
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            ItemType = "video",
            Stage = "unknown",
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Assert
        Assert.Equal("pending", item.TranscriptStatus);
        Assert.Equal("pending", item.SegmentsStatus);
        Assert.Equal("pending", item.ScreenshotsStatus);
        Assert.Equal("pending", item.LinksStatus);
        Assert.Equal("pending", item.ReposStatus);
        Assert.Equal("pending", item.WebsitesStatus);
        Assert.Equal("pending", item.EmbeddingsStatus);
    }

    [Fact]
    public void IngestionItem_UpdatedAtTracksModificationTime()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            ItemType = "video",
            Stage = "unknown",
            Status = "pending",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

        var futureTime = DateTimeOffset.UtcNow.AddSeconds(30);

        // Act
        item.UpdatedAt = futureTime;

        // Assert
        Assert.True(item.UpdatedAt > item.CreatedAt);
    }
}
