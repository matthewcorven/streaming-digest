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

    public Task RunAsync(ModelDownloadCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_queue is null || _logger is null)
        {
            throw new InvalidOperationException("ModelDownloadJob must be activated via dependency injection.");
        }

        var enqueued = _queue.TryEnqueue(command);
        if (enqueued)
        {
            _logger.LogInformation(
                "Model download queued for execution (operation {OperationId}, {Provider}/{ModelId}).",
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
