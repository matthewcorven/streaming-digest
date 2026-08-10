using System.Collections.Concurrent;
using System.Threading.Channels;
using StreamingDigest.Application.Models;

namespace StreamingDigest.Worker.ModelDownload;

/// <summary>
/// Bounded in-memory queue that enforces pull concurrency 1 (single reader hosted service)
/// and de-duplicates pending commands per (provider, modelId). Hangfire jobs may fire
/// multiple times for the same model (retries, duplicate client clicks); only the first
/// enqueue wins until the pipeline completes the entry.
/// </summary>
public sealed class ChannelModelDownloadQueue : IModelDownloadQueue
{
    // A small bound is sufficient: concurrency is 1 and de-dup keeps distinct entries limited
    // to the number of catalog models. A full channel rejects rather than blocking Hangfire.
    private const int BoundedCapacity = 32;

    private readonly Channel<ModelDownloadCommand> _channel = Channel.CreateBounded<ModelDownloadCommand>(
        new BoundedChannelOptions(BoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

    public bool TryEnqueue(ModelDownloadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var key = KeyFor(command.Provider, command.ModelId);
        if (!_pending.TryAdd(key, 0))
        {
            // Already queued or running — de-dup no-op.
            return false;
        }

        if (_channel.Writer.TryWrite(command))
        {
            return true;
        }

        // Bounded channel is full: release the dedup slot so a later retry can enqueue again.
        _pending.TryRemove(key, out _);
        return false;
    }

    /// <summary>Releases the de-dup slot once a command has finished (success or failure).</summary>
    public void Complete(ModelDownloadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _pending.TryRemove(KeyFor(command.Provider, command.ModelId), out _);
    }

    public IAsyncEnumerable<ModelDownloadCommand> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    private static string KeyFor(string provider, string modelId) => $"{provider}:{modelId}";
}
