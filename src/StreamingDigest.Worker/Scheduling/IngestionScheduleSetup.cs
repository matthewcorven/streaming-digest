using Hangfire;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.Worker.Scheduling;

/// <summary>
/// Registers (or removes) the Hangfire recurring ingestion job at worker startup
/// based on <see cref="SchedulerSettings"/> (plan §4 D2 / §10.3, ADR-0011).
///
/// Call <see cref="Apply"/> after <c>app.Build()</c> so Hangfire storage is already
/// initialised before <c>RecurringJob.AddOrUpdate</c> is called.
/// </summary>
public sealed class IngestionScheduleSetup(
    ApplicationConfiguration configuration,
    IRecurringJobManager recurringJobManager,
    ILogger<IngestionScheduleSetup> logger)
{
    /// <summary>
    /// Applies the configured schedule.  When the scheduler is disabled or when the
    /// configured hour/minute falls outside valid ranges, the recurring job is
    /// removed from Hangfire and a warning is emitted.
    /// </summary>
    public void Apply()
    {
        var settings = configuration.Ingestion.Scheduler;

        if (!settings.Enabled)
        {
            recurringJobManager.RemoveIfExists(IngestionJob.RecurringJobId);
            logger.LogInformation(
                "Ingestion scheduler is disabled (ingestion.scheduler.enabled = false). " +
                "Recurring job '{JobId}' removed.", IngestionJob.RecurringJobId);
            return;
        }

        if (settings.ScheduleHour is < 0 or > 23)
        {
            logger.LogWarning(
                "Invalid ingestion.scheduler.scheduleHour ({Hour}); must be 0–23. " +
                "Recurring job not registered.", settings.ScheduleHour);
            return;
        }

        if (settings.ScheduleMinute is < 0 or > 59)
        {
            logger.LogWarning(
                "Invalid ingestion.scheduler.scheduleMinute ({Minute}); must be 0–59. " +
                "Recurring job not registered.", settings.ScheduleMinute);
            return;
        }

        var cron = BuildCron(settings.ScheduleMinute, settings.ScheduleHour);

        recurringJobManager.AddOrUpdate<IngestionJob>(
            IngestionJob.RecurringJobId,
            job => job.ExecuteScheduledAsync(CancellationToken.None),
            cron);

        logger.LogInformation(
            "Ingestion recurring job '{JobId}' registered: daily at {Hour:D2}:{Minute:D2} " +
            "local server time (cron '{Cron}').",
            IngestionJob.RecurringJobId, settings.ScheduleHour, settings.ScheduleMinute, cron);
    }

    /// <summary>
    /// Builds a daily cron expression from a minute + hour.
    /// Exposed for unit testing.
    /// </summary>
    public static string BuildCron(int minute, int hour)
        => $"{minute} {hour} * * *";
}
