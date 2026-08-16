using Hangfire;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Worker.Scheduling;

/// <summary>
/// Unified Hangfire job class for all ingestion pipeline invocations (both recurring
/// and on-demand). This consolidates all ingestion job dispatch into a single job type
/// with parameterized trigger metadata.
///
/// Entry points:
/// - <see cref="ExecuteScheduledAsync"/> — registered as recurring job (6AM)
/// - <see cref="ExecuteOnDemandAsync"/> — enqueued by <see cref="HangfireIngestionJobScheduler"/> for manual/catch-up runs
/// - <see cref="ExecuteAsync"/> (internal) — unified implementation called by both entry points
///
/// ADR-0011: scheduled runs check <see cref="IEmbeddingTransitionChecker"/> and
/// bail out when a transition is active — the Hangfire recurring slot fires as
/// normal, but no ingestion work occurs. The single catch-up run is enqueued by
/// the embedding-regeneration completion path (calling
/// <see cref="IIngestionJobScheduler.EnqueueOnDemandRun"/>), not from here.
/// </summary>
public sealed class IngestionJob(
    IEmbeddingTransitionChecker transitionChecker,
    IIngestionOrchestrator orchestrator,
    IChannelRepository channels,
    ILogger<IngestionJob> logger)
{
    /// <summary>Hangfire recurring-job identifier.</summary>
    public const string RecurringJobId = "ingestion.scheduled";

    // ──────────────────────────────────────────────────────────────────────────
    // Entry points
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recurring entry point. Skips silently when an embedding transition is
    /// active (ADR-0011 pause). The recurring slot still fires; nothing is
    /// persisted or retried on skip.
    ///
    /// Wrapped by: <see cref="ExecuteAsync"/> (unified implementation)
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteScheduledAsync(CancellationToken cancellationToken = default)
    {
        if (await transitionChecker.IsTransitionActiveAsync(cancellationToken))
        {
            logger.LogInformation(
                "Scheduled ingestion skipped: an embedding transition is in progress (ADR-0011). " +
                "A catch-up run will fire automatically when the transition completes.");
            return;
        }

        await ExecuteAsync(
            channelId: null,
            runType: "scheduled",
            triggeredBy: "system",
            isScheduledRecurring: true,
            cancellationToken);
    }

    /// <summary>
    /// On-demand / catch-up entry point. Enqueued by
    /// <see cref="HangfireIngestionJobScheduler.EnqueueOnDemandRun"/> or by the
    /// embedding-regeneration completion path for the single ADR-0011 catch-up.
    /// Manual runs are intentionally <em>not</em> blocked by a transition — the
    /// operator is present and can observe the coverage banner.
    ///
    /// Wrapped by: <see cref="ExecuteAsync"/> (unified implementation)
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteOnDemandAsync(
        Guid? channelId,
        string runType,
        string triggeredBy,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(channelId, runType, triggeredBy, isScheduledRecurring: false, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Unified implementation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unified ingestion execution logic. Called by both <see cref="ExecuteScheduledAsync"/>
    /// and <see cref="ExecuteOnDemandAsync"/> to consolidate job dispatch logic.
    /// </summary>
    private async Task ExecuteAsync(
        Guid? channelId,
        string runType,
        string triggeredBy,
        bool isScheduledRecurring,
        CancellationToken cancellationToken)
    {
        if (channelId.HasValue)
        {
            await RunSingleChannelAsync(channelId.Value, runType, triggeredBy, cancellationToken);
        }
        else
        {
            await RunAllChannelsAsync(runType, triggeredBy, cancellationToken);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task RunAllChannelsAsync(
        string runType,
        string triggeredBy,
        CancellationToken cancellationToken)
    {
        var activeChannels = await channels.GetAllAsync(excludePaused: true, cancellationToken);

        if (activeChannels.Count == 0)
        {
            logger.LogInformation("Ingestion ({RunType}): no active channels configured.", runType);
            return;
        }

        logger.LogInformation(
            "Ingestion ({RunType}): processing {Count} active channel(s) triggered by '{TriggeredBy}'.",
            runType, activeChannels.Count, triggeredBy);

        foreach (var channel in activeChannels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunChannelAsync(channel, runType, triggeredBy, cancellationToken);
        }
    }

    private async Task RunSingleChannelAsync(
        Guid channelId,
        string runType,
        string triggeredBy,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Ingestion ({RunType}): processing channel {ChannelId} triggered by '{TriggeredBy}'.",
            runType, channelId, triggeredBy);

        await orchestrator.RunChannelIngestionAsync(
            new ChannelIngestionRequest
            {
                ChannelId = channelId,
                RunType = runType,
                TriggeredBy = triggeredBy,
            },
            cancellationToken);
    }

    private async Task RunChannelAsync(
        Channel channel,
        string runType,
        string triggeredBy,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await orchestrator.RunChannelIngestionAsync(
                new ChannelIngestionRequest
                {
                    ChannelId = channel.Id,
                    RunType = runType,
                    TriggeredBy = triggeredBy,
                },
                cancellationToken);

            logger.LogInformation(
                "Ingestion ({RunType}) for channel '{Channel}' finished with status '{Status}': " +
                "{VideosIngested} ingested, {VideosSkipped} skipped.",
                runType, channel.YoutubeChannelId, run.Status, run.VideosIngested, run.VideosSkipped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Ingestion ({RunType}) for channel '{Channel}' failed unexpectedly.",
                runType, channel.YoutubeChannelId);
        }
    }
}
