namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Schedules ingestion jobs (plan §4 D2 / §10.3). Implementations back this with
/// Hangfire in the Worker process. The Application layer depends only on this seam
/// so <see cref="Admin.AdminOperationsService"/> can enqueue real jobs without
/// taking a hard Hangfire reference.
/// </summary>
public interface IIngestionJobScheduler
{
    /// <summary>
    /// Enqueues one on-demand ingestion run, optionally scoped to a specific channel.
    /// </summary>
    /// <param name="channelId">
    /// When provided, only that channel is processed. <c>null</c> processes all
    /// non-paused channels.
    /// </param>
    /// <param name="runType">
    /// Run-type label written to the <c>ingestion_runs</c> row (e.g. <c>manual</c>,
    /// <c>backfill</c>).
    /// </param>
    /// <param name="triggeredBy">Actor that triggered the run (user id or <c>system</c>).</param>
    /// <returns>The Hangfire job id.</returns>
    string EnqueueOnDemandRun(Guid? channelId, string runType, string triggeredBy);

    /// <summary>
    /// Registers (or updates) the Hangfire recurring job using the supplied
    /// <paramref name="cronExpression"/>. If <paramref name="cronExpression"/> is
    /// <c>null</c> or empty, the recurring job is removed.
    /// </summary>
    void SetRecurringJob(string? cronExpression);
}
