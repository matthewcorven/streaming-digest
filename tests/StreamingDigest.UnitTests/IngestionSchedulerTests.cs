using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Worker.Scheduling;

namespace StreamingDigest.UnitTests;

/// <summary>
/// Unit tests for the A3 ingestion scheduler (issue #213):
///   - Schedule resolution: cron is built correctly from configured hour/minute.
///   - ADR-0011 pause: <see cref="IngestionJob.ExecuteScheduledAsync"/> skips when a
///     transition is active.
///   - ADR-0011 catch-up: on-demand enqueue via <see cref="IIngestionJobScheduler"/>
///     correctly dispatches a scheduled-type run.
///   - <see cref="IngestionScheduleSetup.Apply"/> registers/removes the recurring job.
/// </summary>
public sealed class IngestionSchedulerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // IngestionScheduleSetup.BuildCron
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 6, "0 6 * * *")]
    [InlineData(30, 14, "30 14 * * *")]
    [InlineData(0, 0, "0 0 * * *")]
    [InlineData(59, 23, "59 23 * * *")]
    public void BuildCron_ProducesCorrectExpression(int minute, int hour, string expected)
    {
        var cron = IngestionScheduleSetup.BuildCron(minute, hour);
        Assert.Equal(expected, cron);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IngestionScheduleSetup.Apply — recurring job registration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_WhenEnabled_RegistersRecurringJobWithCorrectCron()
    {
        var config = ConfigWith(enabled: true, hour: 6, minute: 0);
        var manager = new Mock<IRecurringJobManager>();

        var setup = new IngestionScheduleSetup(config, manager.Object, NullLogger<IngestionScheduleSetup>.Instance);
        setup.Apply();

        manager.Verify(m => m.AddOrUpdate(
            IngestionJob.RecurringJobId,
            It.IsAny<Job>(),
            "0 6 * * *",
            It.IsAny<RecurringJobOptions>()),
            Times.Once);
    }

    [Fact]
    public void Apply_WhenDisabled_RemovesRecurringJob()
    {
        var config = ConfigWith(enabled: false, hour: 6, minute: 0);
        var manager = new Mock<IRecurringJobManager>();

        var setup = new IngestionScheduleSetup(config, manager.Object, NullLogger<IngestionScheduleSetup>.Instance);
        setup.Apply();

        manager.Verify(m => m.RemoveIfExists(IngestionJob.RecurringJobId), Times.Once);
        manager.Verify(m => m.AddOrUpdate(
            It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()),
            Times.Never);
    }

    [Theory]
    [InlineData(-1, 0)]   // hour out of range
    [InlineData(24, 0)]
    [InlineData(6, -1)]   // minute out of range
    [InlineData(6, 60)]
    public void Apply_WhenInvalidSchedule_DoesNotRegisterJob(int hour, int minute)
    {
        var config = ConfigWith(enabled: true, hour: hour, minute: minute);
        var manager = new Mock<IRecurringJobManager>();

        var setup = new IngestionScheduleSetup(config, manager.Object, NullLogger<IngestionScheduleSetup>.Instance);
        setup.Apply();

        manager.Verify(m => m.AddOrUpdate(
            It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ADR-0011 pause: IngestionJob.ExecuteScheduledAsync skips when active
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScheduledAsync_WhenTransitionActive_SkipsWithoutCallingOrchestrator()
    {
        var transitionChecker = new Mock<IEmbeddingTransitionChecker>();
        transitionChecker.Setup(t => t.IsTransitionActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var orchestrator = new Mock<IIngestionOrchestrator>();
        var channels = new Mock<IChannelRepository>();

        var job = new IngestionJob(
            transitionChecker.Object,
            orchestrator.Object,
            channels.Object,
            NullLogger<IngestionJob>.Instance);

        await job.ExecuteScheduledAsync(CancellationToken.None);

        orchestrator.Verify(o => o.RunChannelIngestionAsync(
            It.IsAny<ChannelIngestionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_WhenTransitionNotActive_ProcessesAllActiveChannels()
    {
        var transitionChecker = new Mock<IEmbeddingTransitionChecker>();
        transitionChecker.Setup(t => t.IsTransitionActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var channelA = new Channel { Id = Guid.NewGuid(), YoutubeChannelId = "UC_A", NameOriginal = "A" };
        var channelB = new Channel { Id = Guid.NewGuid(), YoutubeChannelId = "UC_B", NameOriginal = "B" };
        var channels = new Mock<IChannelRepository>();
        channels.Setup(c => c.GetAllAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([channelA, channelB]);

        var runs = new List<ChannelIngestionRequest>();
        var orchestrator = new Mock<IIngestionOrchestrator>();
        orchestrator
            .Setup(o => o.RunChannelIngestionAsync(It.IsAny<ChannelIngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChannelIngestionRequest, CancellationToken>((r, _) => runs.Add(r))
            .ReturnsAsync(new IngestionRun { Id = Guid.NewGuid(), RunType = "scheduled", Status = "completed" });

        var job = new IngestionJob(
            transitionChecker.Object,
            orchestrator.Object,
            channels.Object,
            NullLogger<IngestionJob>.Instance);

        await job.ExecuteScheduledAsync(CancellationToken.None);

        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal("scheduled", r.RunType));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ADR-0011 catch-up: on-demand enqueue wires correct run type
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnqueueOnDemandRun_WithNullChannel_EnqueuesAllChannelsJob()
    {
        var client = new Mock<IBackgroundJobClient>();
        client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-1");

        var manager = new Mock<IRecurringJobManager>();
        var scheduler = new HangfireIngestionJobScheduler(client.Object, manager.Object);

        var jobId = scheduler.EnqueueOnDemandRun(channelId: null, runType: "scheduled", triggeredBy: "system.catchup");

        Assert.Equal("job-1", jobId);
        client.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
    }

    [Fact]
    public void EnqueueOnDemandRun_WithSpecificChannel_EnqueuesChannelJob()
    {
        var channelId = Guid.NewGuid();
        var client = new Mock<IBackgroundJobClient>();
        client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-2");

        var manager = new Mock<IRecurringJobManager>();
        var scheduler = new HangfireIngestionJobScheduler(client.Object, manager.Object);

        var jobId = scheduler.EnqueueOnDemandRun(channelId, runType: "manual", triggeredBy: "admin");

        Assert.Equal("job-2", jobId);
        client.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ApplicationConfiguration ConfigWith(bool enabled, int hour, int minute) =>
        new()
        {
            Ingestion = new IngestionSettings
            {
                Scheduler = new SchedulerSettings
                {
                    Enabled = enabled,
                    ScheduleHour = hour,
                    ScheduleMinute = minute
                }
            }
        };
}
