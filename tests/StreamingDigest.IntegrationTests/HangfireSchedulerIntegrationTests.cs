extern alias StreamingDigestWorker;

using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application.Configuration;
using StreamingDigestWorker::StreamingDigest.Worker.Scheduling;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// A3 integration tests (issue #213): verifies that the Hangfire scheduler
/// infrastructure works end-to-end against Hangfire's in-memory storage —
/// no Docker or Postgres required.
///
/// Serialized via <see cref="HangfireSchedulerCollection"/> because
/// <see cref="Hangfire.JobStorage.Current"/> is a process-wide static;
/// running these tests in parallel would cause cross-test interference.
///
/// Covers:
///   - <see cref="IngestionScheduleSetup.Apply"/> registers the recurring job.
///   - <see cref="HangfireIngestionJobScheduler.EnqueueOnDemandRun"/> enqueues
///     exactly one Hangfire background job.
///   - Disabling the scheduler removes the recurring job from Hangfire.
/// </summary>
[Collection("HangfireScheduler")]
public sealed class HangfireSchedulerIntegrationTests : IDisposable
{
    private readonly MemoryStorage _storage;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireSchedulerIntegrationTests()
    {
        // Reset the Hangfire-global log provider to a no-op before touching any
        // Hangfire internals. Without this, a stale AspNetCoreLogProvider (set by
        // an earlier test that used the ASP.NET Core host) may reference a
        // disposed LoggerFactory and throw ObjectDisposedException when
        // RecurringJobManager / BackgroundJobClient log internally.
        GlobalConfiguration.Configuration.UseLogProvider(new NoOpHangfireLogProvider());

        _storage = new MemoryStorage();
        // Use explicit-storage overloads so JobStorage.Current is never touched.
        _recurringJobManager = new RecurringJobManager(_storage);
        _backgroundJobClient = new BackgroundJobClient(_storage);
    }

    public void Dispose() { /* MemoryStorage is not IDisposable */ }

    [Fact]
    public void Apply_RegistersRecurringJob_InHangfireStorage()
    {
        var config = ConfigWith(enabled: true, hour: 6, minute: 0);
        var setup = new IngestionScheduleSetup(
            config,
            _recurringJobManager,
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
        // First register, then disable.
        var enabledConfig = ConfigWith(enabled: true, hour: 6, minute: 0);
        new IngestionScheduleSetup(enabledConfig, _recurringJobManager, NullLogger<IngestionScheduleSetup>.Instance).Apply();

        var disabledConfig = ConfigWith(enabled: false, hour: 6, minute: 0);
        new IngestionScheduleSetup(disabledConfig, _recurringJobManager, NullLogger<IngestionScheduleSetup>.Instance).Apply();

        using var connection = _storage.GetConnection();
        var jobs = connection.GetRecurringJobs();
        Assert.DoesNotContain(jobs, j => j.Id == IngestionJob.RecurringJobId);
    }

    [Fact]
    public void EnqueueOnDemandRun_WithNullChannel_CreatesEnqueuedJob()
    {
        var scheduler = new HangfireIngestionJobScheduler(_backgroundJobClient, _recurringJobManager);

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
        var scheduler = new HangfireIngestionJobScheduler(_backgroundJobClient, _recurringJobManager);

        var jobId = scheduler.EnqueueOnDemandRun(channelId, runType: "backfill", triggeredBy: "admin");

        Assert.False(string.IsNullOrWhiteSpace(jobId));

        using var connection = _storage.GetConnection();
        var jobData = connection.GetJobData(jobId);
        Assert.NotNull(jobData);
        Assert.Equal(EnqueuedState.StateName, jobData.State);
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

    // ── Hangfire log plumbing ─────────────────────────────────────────────────

    private sealed class NoOpHangfireLogProvider : Hangfire.Logging.ILogProvider
    {
        public Hangfire.Logging.ILog GetLogger(string name) => new NoOpLog();
    }

    private sealed class NoOpLog : Hangfire.Logging.ILog
    {
        public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null)
            => logLevel >= Hangfire.Logging.LogLevel.Error; // return true = "I can log this level"
    }
}

[CollectionDefinition("HangfireScheduler", DisableParallelization = true)]
public sealed class HangfireSchedulerCollection { }
