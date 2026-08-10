using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.UnitTests;

public sealed class DigestAssemblyServiceTests
{
    [Fact]
    public void DigestAssemblyRequest_defaults_high_signal_threshold_to_calibrated_value()
    {
        var request = new DigestAssemblyRequest();

        Assert.Equal(70, request.HighSignalThresholdPercent);
    }

    [Fact]
    public async Task Assemble_and_persist_digest_with_transition_and_threshold_rules()
    {
        var (context, databaseFilePath) = await CreateContextAsync();

        try
        {
            var service = new DigestAssemblyService(context);
            var ingestionRunId = Guid.NewGuid();

            var request = new DigestAssemblyRequest
            {
                IngestionRunId = ingestionRunId,
                RunType = "backfill",
                NewVideos = new[] { new DigestItem { Id = "video-1", Label = "New video" } },
                NewResources = new[] { new DigestResource { Id = "repo-1", Name = "Example repo", ResourceType = "repository" } },
                HighSignalMatches = new[]
                {
                    new HighSignalMatch { Id = "match-1", Label = "High match", SimilarityPercent = 91 },
                    new HighSignalMatch { Id = "match-2", Label = "Low match", SimilarityPercent = 70 }
                },
                FailedItems = new[] { new DigestItem { Id = "fail-1", Label = "Failed video" } },
                SkippedItems = new[] { new DigestItem { Id = "skip-1", Label = "Skipped video" } },
                ActiveDeferments = new[] { new ActiveDeferment { Id = "defer-1", Label = "Needs review", Reason = "rate limit" } },
                HighSignalThresholdPercent = 80,
                IsEmbeddingTransitionActive = true,
                IsBackfillRun = true
            };

            var digest = await service.AssembleAndPersistAsync(request);

            Assert.Equal(ingestionRunId, digest.IngestionRunId);
            Assert.Equal("backfill", digest.RunType);
            var payload = DigestPayloadSerializer.Deserialize(digest.PayloadJson);
            Assert.Single(payload.NewVideos);
            Assert.Single(payload.NewResources);
            Assert.Empty(payload.HighSignalMatches);
            Assert.True(payload.HighSignalEvaluationSkipped);
            Assert.Equal(80, payload.HighSignalThresholdPercent);
            Assert.True(payload.IsBackfillRun);
        }
        finally
        {
            await context.DisposeAsync();
            DeleteDatabaseFile(databaseFilePath);
        }
    }

    [Fact]
    public async Task Assemble_and_persist_digest_persists_notification_audit_and_outbox_on_success()
    {
        var (context, databaseFilePath) = await CreateContextAsync();

        try
        {
            var fakeNotificationService = new FakeMatrixNotificationService(successOnAttempt: 1, providerMessageId: "$event-success");
            var notificationDispatchService = new NotificationDispatchService(context, fakeNotificationService);
            var service = new DigestAssemblyService(context, notificationDispatchService: notificationDispatchService);

            var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "scheduled");
            var digest = await service.AssembleAndPersistAsync(request);

            var notification = await context.Notifications.SingleAsync();
            var outboxMessage = await context.OutboxMessages.SingleAsync();

            Assert.Equal(digest.IngestionRunId, notification.IngestionRunId);
            Assert.Equal(request.OperationId, notification.OperationId);
            Assert.Equal("matrix", notification.Provider);
            Assert.Equal("sent", notification.Status);
            Assert.Equal("$event-success", notification.ProviderMessageId);
            Assert.Equal(1, notification.AttemptCount);
            Assert.Equal("sent", outboxMessage.Status);
            Assert.Equal(1, outboxMessage.AttemptCount);
            Assert.NotNull(outboxMessage.SentAt);
        }
        finally
        {
            await context.DisposeAsync();
            DeleteDatabaseFile(databaseFilePath);
        }
    }

    [Fact]
    public async Task Assemble_and_persist_digest_keeps_only_fixture_matches_that_clear_the_configured_threshold()
    {
        var (context, databaseFilePath) = await CreateContextAsync();

        try
        {
            var fixture = LoadHighSignalCalibrationFixture();
            var service = new DigestAssemblyService(context);
            var request = new DigestAssemblyRequest
            {
                IngestionRunId = Guid.NewGuid(),
                HighSignalThresholdPercent = fixture.RecommendedThreshold.ThresholdPercent,
                HighSignalMatches = fixture.Cases
                    .SelectMany(testCase => new[]
                    {
                        new HighSignalMatch
                        {
                            Id = $"{testCase.Id}-intended",
                            Label = testCase.IntendedCandidateTitle,
                            SimilarityPercent = Math.Round(testCase.IntendedSimilarity * 100, 2)
                        },
                        new HighSignalMatch
                        {
                            Id = $"{testCase.Id}-best-other",
                            Label = testCase.BestOther.Title,
                            SimilarityPercent = Math.Round(testCase.BestOther.Similarity * 100, 2)
                        }
                    })
                    .ToArray()
            };

            var digest = await service.AssembleAndPersistAsync(request);
            var payload = DigestPayloadSerializer.Deserialize(digest.PayloadJson);

            Assert.Equal(fixture.RecommendedThreshold.OwnPassCount, payload.HighSignalMatches.Count);
            Assert.Equal(
                fixture.Cases.Select(testCase => testCase.IntendedCandidateTitle).OrderBy(title => title, StringComparer.Ordinal).ToArray(),
                payload.HighSignalMatches.Select(match => match.Label).OrderBy(label => label, StringComparer.Ordinal).ToArray());
            Assert.All(payload.HighSignalMatches, match => Assert.EndsWith("-intended", match.Id, StringComparison.Ordinal));
        }
        finally
        {
            await context.DisposeAsync();
            DeleteDatabaseFile(databaseFilePath);
        }
    }

    [Fact]
    public async Task Assemble_and_persist_digest_creates_retryable_outbox_state_when_delivery_fails()
    {
        var (context, databaseFilePath) = await CreateContextAsync();

        try
        {
            var fakeNotificationService = new FakeMatrixNotificationService(successOnAttempt: 2, providerMessageId: "$event-retry");
            var notificationDispatchService = new NotificationDispatchService(context, fakeNotificationService);
            var service = new DigestAssemblyService(context, notificationDispatchService: notificationDispatchService);

            var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "scheduled");
            var digest = await service.AssembleAndPersistAsync(request);

            var notification = await context.Notifications.SingleAsync();
            var outboxMessage = await context.OutboxMessages.SingleAsync();

            Assert.Equal(digest.IngestionRunId, notification.IngestionRunId);
            Assert.Equal("pending", notification.Status);
            Assert.Equal(1, notification.AttemptCount);
            Assert.Equal("pending", outboxMessage.Status);
            Assert.Equal(1, outboxMessage.AttemptCount);
            Assert.NotNull(notification.NextRetryAt);
            Assert.NotNull(outboxMessage.NextAttemptAt);
            Assert.Contains("simulated", notification.ErrorSummary);
        }
        finally
        {
            await context.DisposeAsync();
            DeleteDatabaseFile(databaseFilePath);
        }
    }

    [Fact]
    public async Task DispatchPendingAsync_retries_failed_notification_and_marks_it_sent()
    {
        var (context, databaseFilePath) = await CreateContextAsync();

        try
        {
            var fakeNotificationService = new FakeMatrixNotificationService(successOnAttempt: 2, providerMessageId: "$event-retry");
            var notificationDispatchService = new NotificationDispatchService(context, fakeNotificationService);
            var service = new DigestAssemblyService(context, notificationDispatchService: notificationDispatchService);

            var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "scheduled");
            await service.AssembleAndPersistAsync(request);

            var notification = await context.Notifications.SingleAsync();
            Assert.Equal("pending", notification.Status);
            Assert.Equal(1, notification.AttemptCount);

            await notificationDispatchService.DispatchPendingAsync();

            notification = await context.Notifications.SingleAsync();
            var outboxMessage = await context.OutboxMessages.SingleAsync();

            Assert.Equal("sent", notification.Status);
            Assert.Equal("$event-retry", notification.ProviderMessageId);
            Assert.Equal(2, notification.AttemptCount);
            Assert.Equal("sent", outboxMessage.Status);
            Assert.Equal(2, outboxMessage.AttemptCount);
        }
        finally
        {
            await context.DisposeAsync();
            DeleteDatabaseFile(databaseFilePath);
        }
    }

    private static async Task<(StreamingDigestDbContext Context, string DatabaseFilePath)> CreateContextAsync()
    {
        var databaseFilePath = Path.Combine(Path.GetTempPath(), $"streaming-digest-tests-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var context = new StreamingDigestDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (context, databaseFilePath);
    }

    private static void DeleteDatabaseFile(string databaseFilePath)
    {
        try
        {
            if (File.Exists(databaseFilePath))
            {
                File.Delete(databaseFilePath);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static DigestAssemblyRequest CreateRequest(Guid ingestionRunId, Guid operationId, string runType)
        => new()
        {
            IngestionRunId = ingestionRunId,
            OperationId = operationId,
            RunType = runType,
            NotificationTarget = "#matrix"
        };

    private static HighSignalCalibrationFixture LoadHighSignalCalibrationFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "verification", "12.x-high-signal-threshold-calibration.json")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the high-signal threshold calibration fixture.");
        }

        var path = Path.Combine(directory.FullName, "docs", "verification", "12.x-high-signal-threshold-calibration.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HighSignalCalibrationFixture>(json) ?? throw new InvalidOperationException("Could not deserialize the high-signal threshold calibration fixture.");
    }

    private sealed class FakeMatrixNotificationService(int successOnAttempt, string? providerMessageId = null) : IMatrixNotificationService
    {
        private int _attempts;

        public bool IsEnabled => true;

        public Task<MatrixSendResult> SendDigestSummaryAsync(Digest digest, CancellationToken cancellationToken = default)
        {
            _attempts++;
            if (_attempts < successOnAttempt)
            {
                return Task.FromResult(new MatrixSendResult(false, "simulated notifier failure"));
            }

            return Task.FromResult(new MatrixSendResult(true, "Matrix message sent.", "{\"event_id\":\"" + providerMessageId + "\"}", providerMessageId));
        }

        public Task<MatrixSendResult> SendTestNotificationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MatrixSendResult(true, "Matrix message sent.", "{\"event_id\":\"$test\"}", "$test"));
    }

    private sealed class HighSignalCalibrationFixture
    {
        public List<HighSignalCalibrationCase> Cases { get; set; } = new();

        public HighSignalRecommendedThreshold RecommendedThreshold { get; set; } = new();
    }

    private sealed class HighSignalCalibrationCase
    {
        public string Id { get; set; } = string.Empty;

        public string IntendedCandidateTitle { get; set; } = string.Empty;

        public double IntendedSimilarity { get; set; }

        public HighSignalCalibrationBestOther BestOther { get; set; } = new();
    }

    private sealed class HighSignalCalibrationBestOther
    {
        public string Title { get; set; } = string.Empty;

        public double Similarity { get; set; }
    }

    private sealed class HighSignalRecommendedThreshold
    {
        public double ThresholdPercent { get; set; }

        public int OwnPassCount { get; set; }
    }
}
