using Hangfire;
using StreamingDigest.Application.Models;

namespace StreamingDigest.Worker.ModelDownload;

/// <summary>
/// Hangfire job entry point for model downloads. The job's only job is the durable handoff:
/// it pushes the command into the bounded execution channel, which enforces pull
/// concurrency 1 and de-dup. Ollama pull streaming never runs inside the Hangfire job.
/// The API references this type when enqueueing so Hangfire serializes a strongly-typed job.
/// </summary>
public sealed class ModelDownloadJob
{
    private readonly ChannelModelDownloadQueue? _queue;
    private readonly ILogger<ModelDownloadJob>? _logger;

    // Parameterless ctor exists so the API assembly (which never activates the job) can
    // reference the type for Enqueue without constructing it; Hangfire uses the DI ctor.
    public ModelDownloadJob()
    {
    }

    public ModelDownloadJob(ChannelModelDownloadQueue queue, ILogger<ModelDownloadJob> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Retries are disabled: the API persists operations/model_runtime_state=queued before
    // enqueueing, so a Hangfire retry after a transient failure would re-push a command for an
    // operation the pipeline may already have marked failed (state flicker). The API process
    // never runs a Hangfire server (only the worker does), so only worker-side failures matter.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(ModelDownloadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_queue is null || _logger is null)
        {
            throw new InvalidOperationException("ModelDownloadJob must be activated via dependency injection.");
        }

        var enqueued = _queue.TryEnqueue(command, out var droppedBecauseFull);
        if (enqueued)
        {
            _logger.LogInformation(
                "Model download queued for execution (operation {OperationId}, {Provider}/{ModelId}).",
                command.OperationId,
                command.Provider,
                command.ModelId);
        }
        else if (droppedBecauseFull)
        {
            // The API already persisted operations + model_runtime_state=queued for this command;
            // a full-channel drop leaves that row queued until the queue drains and the user
            // retries (WS-6/WS-8 recovery path). Warning so the orphaned row is diagnosable.
            _logger.LogWarning(
                "Model download execution channel is full; command dropped (operation {OperationId}, {Provider}/{ModelId}). The persisted operation stays queued until retry.",
                command.OperationId,
                command.Provider,
                command.ModelId);
        }
        else
        {
            _logger.LogInformation(
                "Model download already queued or running; skipping duplicate (operation {OperationId}, {Provider}/{ModelId}).",
                command.OperationId,
                command.Provider,
                command.ModelId);
        }

        return Task.CompletedTask;
    }
}
