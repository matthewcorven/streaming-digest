namespace StreamingDigest.Application.Models;

/// <summary>
/// Worker-local execution queue for model downloads. Backed by a bounded
/// <see cref="System.Threading.Channels.Channel{T}"/> with pull concurrency 1; enqueueing a
/// command for a model that is already queued or running is a de-dup no-op and returns false.
/// </summary>
public interface IModelDownloadQueue
{
    /// <summary>
    /// Attempts to enqueue a download command. Returns false when the same (provider, modelId)
    /// is already pending or in flight, or when the bounded channel is full.
    /// </summary>
    bool TryEnqueue(ModelDownloadCommand command);

    /// <summary>Reads queued commands until cancellation. Used by the hosted execution service.</summary>
    IAsyncEnumerable<ModelDownloadCommand> ReadAllAsync(CancellationToken cancellationToken);
}
