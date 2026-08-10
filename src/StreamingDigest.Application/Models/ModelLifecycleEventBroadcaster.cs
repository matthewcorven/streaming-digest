using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace StreamingDigest.Application.Models;

/// <summary>
/// Default <see cref="IModelLifecycleEventBroadcaster"/> backed by one bounded channel per
/// subscriber. Events are appended in publish order; a subscriber that falls behind past the
/// buffer capacity is dropped (its channel is completed and removed) so a stalled client
/// cannot apply back-pressure to publishers or other subscribers. Dropped clients detect the
/// completed stream and reconcile via <c>GET /api/models/status</c>.
/// </summary>
public sealed class ModelLifecycleEventBroadcaster : IModelLifecycleEventBroadcaster
{
    private const int SubscriberBufferCapacity = 256;

    private readonly object _gate = new();
    private readonly List<Channel<ModelLifecycleEvent>> _subscribers = [];

    /// <summary>
    /// Number of active subscribers. Exposed for tests that must wait until the SSE endpoint
    /// has registered before publishing; the broadcaster has no replay and no other
    /// "subscribed" signal.
    /// </summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    public void Publish(ModelLifecycleEvent modelEvent)
    {
        ArgumentNullException.ThrowIfNull(modelEvent);

        Channel<ModelLifecycleEvent>[] subscribers;
        lock (_gate)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(modelEvent))
            {
                // Subscriber fell behind the buffer capacity: drop it rather than block
                // publishers or other subscribers. The client sees the stream complete and
                // resynchronizes through the status snapshot endpoint.
                subscriber.Writer.TryComplete();
                RemoveSubscriber(subscriber);
            }
        }
    }

    public async IAsyncEnumerable<ModelLifecycleEvent> Subscribe(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<ModelLifecycleEvent>(new BoundedChannelOptions(SubscriberBufferCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var modelEvent))
                {
                    yield return modelEvent;
                }
            }
        }
        finally
        {
            RemoveSubscriber(channel);
        }
    }

    private void RemoveSubscriber(Channel<ModelLifecycleEvent> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(channel);
        }
    }
}
