using Hangfire;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Orchestration;

namespace StreamingDigest.Worker.ChannelIngestion;

/// <summary>
/// Hangfire job entry point for channel ingestion runs triggered by admin operations
/// (run now, backfill, retry, reprocess). Delegates directly to
/// <see cref="IIngestionOrchestrator.RunChannelIngestionAsync"/>, which owns the run
/// lifecycle: create → process videos → finalize.
/// </summary>
/// <remarks>
/// Retries are disabled: the API persists an <c>operations</c> record and sets status
/// to <c>accepted</c> before enqueueing. A Hangfire retry after a transient failure
/// would re-run a channel whose run record already exists or is partially complete.
/// Recovery for genuine failures is via the admin retry/reprocess verbs.
/// </remarks>
public sealed class ChannelIngestionJob
{
    private readonly IIngestionOrchestrator? _orchestrator;
    private readonly ILogger<ChannelIngestionJob>? _logger;

    // Parameterless ctor exists so the API assembly (which never activates the job) can
    // reference the type for Enqueue without constructing it; Hangfire uses the DI ctor.
    public ChannelIngestionJob()
    {
    }

    public ChannelIngestionJob(IIngestionOrchestrator orchestrator, ILogger<ChannelIngestionJob> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(ChannelIngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_orchestrator is null || _logger is null)
        {
            throw new InvalidOperationException("ChannelIngestionJob must be activated via dependency injection.");
        }

        _logger.LogInformation(
            "Channel ingestion job starting (channel {ChannelId}, runType {RunType}, operationId {OperationId}, isReprocess {IsReprocess}).",
            request.ChannelId,
            request.RunType,
            request.OperationId,
            request.IsReprocessRequest);

        var run = await _orchestrator.RunChannelIngestionAsync(request, cancellationToken);

        _logger.LogInformation(
            "Channel ingestion job completed (channel {ChannelId}, run {RunId}, status {Status}, videosIngested {VideosIngested}).",
            request.ChannelId,
            run.Id,
            run.Status,
            run.VideosIngested);
    }
}
