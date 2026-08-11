using Hangfire;
using StreamingDigest.Application.Orchestration;

namespace StreamingDigest.Worker.Scheduling;

/// <summary>
/// Hangfire-backed implementation of <see cref="IIngestionJobScheduler"/>.
/// Wraps <see cref="IBackgroundJobClient"/> (on-demand) and
/// <see cref="IRecurringJobManager"/> (schedule) so the Application layer stays
/// free of a Hangfire reference.
/// </summary>
public sealed class HangfireIngestionJobScheduler(
    IBackgroundJobClient backgroundJobClient,
    IRecurringJobManager recurringJobManager) : IIngestionJobScheduler
{
    /// <inheritdoc />
    public string EnqueueOnDemandRun(Guid? channelId, string runType, string triggeredBy)
        => backgroundJobClient.Enqueue<IngestionJob>(
            job => job.ExecuteOnDemandAsync(channelId, runType, triggeredBy, CancellationToken.None));

    /// <inheritdoc />
    public void SetRecurringJob(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            recurringJobManager.RemoveIfExists(IngestionJob.RecurringJobId);
            return;
        }

        recurringJobManager.AddOrUpdate<IngestionJob>(
            IngestionJob.RecurringJobId,
            job => job.ExecuteScheduledAsync(CancellationToken.None),
            cronExpression);
    }
}
