extern alias StreamingDigestWorker;

using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application.Configuration;
using StreamingDigestWorker::StreamingDigest.Worker.Scheduling;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// A3 integration tests (issue #213): verifies that the Hangfire scheduler
/// infrastructure works end-to-end against Hangfire's in-memory storage —
/// no Docker or Postgres required.
///
/// Covers:
///   - <see cref="IngestionScheduleSetup.Apply"/> registers the recurring job.
///   - <see cref="HangfireIngestionJobScheduler.EnqueueOnDemandRun"/> enqueues
///     exactly one Hangfire background job.
///   - Disabling the scheduler removes the recurring job from Hangfire.
/// </summary>
public sealed class HangfireSchedulerIntegrationTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly JobStorage _storage;

    public HangfireSchedulerIntegrationTests()
    {
        var memoryStorage = new MemoryStorage();
        JobStorage.Current = memoryStorage;
        _storage = memoryStorage;

        var sc = new ServiceCollection();
        sc.AddHangfire(cfg => cfg.UseStorage(memoryStorage));
        sc.AddHangfireServer();
        sc.AddSingleton<IRecurringJobManager, RecurringJobManager>();
        sc.AddSingleton<IBackgroundJobClient, BackgroundJobClient>();
        _services = sc.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Apply_RegistersRecurringJob_InHangfireStorage()
    {
        var config = ConfigWith(enabled: true, hour: 6, minute: 0);
        var setup = new IngestionScheduleSetup(
            config,
            _services.GetRequiredService<IRecurringJobManager>(),
            NullLogger<IngestionScheduleSetup>.Instance);

        setup.Apply();

        using var connection = _storage.GetConnection();
        var jobs = connection.GetRecurringJobs();
        var registered = jobs.FirstOrDefault(j => j.Id == IngestionJob.RecurringJobId);

        Assert.NotNull(registered);
        Assert.Equal("0 6 * * *", registered.Cron);
    }

    [Fact]
    public void Apply_WhenDisabled_RemovesRecurringJobFromHangfire()
    {
        var manager = _services.GetRequiredService<IRecurringJobManager>();

        // First register, then disable.
        var enabledConfig = ConfigWith(enabled: true, hour: 6, minute: 0);
        new IngestionScheduleSetup(enabledConfig, manager, NullLogger<IngestionScheduleSetup>.Instance).Apply();

        var disabledConfig = ConfigWith(enabled: false, hour: 6, minute: 0);
        new IngestionScheduleSetup(disabledConfig, manager, NullLogger<IngestionScheduleSetup>.Instance).Apply();

        using var connection = _storage.GetConnection();
        var jobs = connection.GetRecurringJobs();
        Assert.DoesNotContain(jobs, j => j.Id == IngestionJob.RecurringJobId);
    }

    [Fact]
    public void EnqueueOnDemandRun_WithNullChannel_CreatesEnqueuedJob()
    {
        var scheduler = new HangfireIngestionJobScheduler(
            _services.GetRequiredService<IBackgroundJobClient>(),
            _services.GetRequiredService<IRecurringJobManager>());

        var jobId = scheduler.EnqueueOnDemandRun(channelId: null, runType: "manual", triggeredBy: "admin");

        Assert.False(string.IsNullOrWhiteSpace(jobId));

        using var connection = _storage.GetConnection();
        var jobData = connection.GetJobData(jobId);
        Assert.NotNull(jobData);
        Assert.Equal(EnqueuedState.StateName, jobData.State);
    }

    [Fact]
    public void EnqueueOnDemandRun_WithSpecificChannel_CreatesEnqueuedJob()
    {
        var channelId = Guid.NewGuid();
        var scheduler = new HangfireIngestionJobScheduler(
            _services.GetRequiredService<IBackgroundJobClient>(),
            _services.GetRequiredService<IRecurringJobManager>());

        var jobId = scheduler.EnqueueOnDemandRun(channelId, runType: "backfill", triggeredBy: "admin");

        Assert.False(string.IsNullOrWhiteSpace(jobId));

        using var connection = _storage.GetConnection();
        var jobData = connection.GetJobData(jobId);
        Assert.NotNull(jobData);
        Assert.Equal(EnqueuedState.StateName, jobData.State);
        // Verify the job args include the channel id.
        Assert.Contains(jobData.Job.Args, a => a is Guid g && g == channelId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationConfiguration ConfigWith(bool enabled, int hour, int minute) => new()
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
