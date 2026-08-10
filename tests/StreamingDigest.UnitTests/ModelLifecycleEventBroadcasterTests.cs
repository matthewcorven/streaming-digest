using StreamingDigest.Application.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace StreamingDigest.UnitTests;

public sealed class ModelLifecycleEventBroadcasterTests
{
    [Fact]
    public async Task Subscriber_receives_one_ordered_event_per_publish()
    {
        var broadcaster = new ModelLifecycleEventBroadcaster(NullLogger<ModelLifecycleEventBroadcaster>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var received = new List<ModelLifecycleEvent>();
        var subscription = Task.Run(async () =>
        {
            await foreach (var modelEvent in broadcaster.Subscribe(cts.Token))
            {
                received.Add(modelEvent);
                if (received.Count == 3)
                {
                    return;
                }
            }
        }, cts.Token);

        // Give the subscription a moment to register before publishing.
        await Task.Delay(50, cts.Token);

        var first = new ModelLifecycleEvent("model.status", "{\"status\":\"queued\"}", DateTimeOffset.UtcNow);
        var second = new ModelLifecycleEvent("operation.status", "{\"status\":\"running\"}", DateTimeOffset.UtcNow);
        var third = new ModelLifecycleEvent("operation.completed", "{\"status\":\"ready\"}", DateTimeOffset.UtcNow);

        broadcaster.Publish(first);
        broadcaster.Publish(second);
        broadcaster.Publish(third);

        await subscription.WaitAsync(cts.Token);

        Assert.Equal(3, received.Count);
        Assert.Same(first, received[0]);
        Assert.Same(second, received[1]);
        Assert.Same(third, received[2]);
    }

    [Fact]
    public async Task Each_subscriber_receives_its_own_copy_in_publish_order()
    {
        var broadcaster = new ModelLifecycleEventBroadcaster(NullLogger<ModelLifecycleEventBroadcaster>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var firstSubscriber = new List<ModelLifecycleEvent>();
        var secondSubscriber = new List<ModelLifecycleEvent>();

        var firstSubscription = Task.Run(async () =>
        {
            await foreach (var modelEvent in broadcaster.Subscribe(cts.Token))
            {
                firstSubscriber.Add(modelEvent);
                if (firstSubscriber.Count == 2)
                {
                    return;
                }
            }
        }, cts.Token);

        var secondSubscription = Task.Run(async () =>
        {
            await foreach (var modelEvent in broadcaster.Subscribe(cts.Token))
            {
                secondSubscriber.Add(modelEvent);
                if (secondSubscriber.Count == 2)
                {
                    return;
                }
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token);

        var first = new ModelLifecycleEvent("model.status", "{\"status\":\"running\"}", DateTimeOffset.UtcNow);
        var second = new ModelLifecycleEvent("model.status", "{\"status\":\"ready\"}", DateTimeOffset.UtcNow);

        broadcaster.Publish(first);
        broadcaster.Publish(second);

        await Task.WhenAll(firstSubscription, secondSubscription).WaitAsync(cts.Token);

        Assert.Equal(new[] { first, second }, firstSubscriber);
        Assert.Equal(new[] { first, second }, secondSubscriber);
    }

    [Fact]
    public async Task Events_published_before_subscription_are_not_replayed()
    {
        var broadcaster = new ModelLifecycleEventBroadcaster(NullLogger<ModelLifecycleEventBroadcaster>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        broadcaster.Publish(new ModelLifecycleEvent("model.status", "{\"status\":\"queued\"}", DateTimeOffset.UtcNow));

        var received = new List<ModelLifecycleEvent>();
        var subscription = Task.Run(async () =>
        {
            await foreach (var modelEvent in broadcaster.Subscribe(cts.Token))
            {
                received.Add(modelEvent);
                if (received.Count == 1)
                {
                    return;
                }
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token);
        var afterSubscribe = new ModelLifecycleEvent("model.status", "{\"status\":\"running\"}", DateTimeOffset.UtcNow);
        broadcaster.Publish(afterSubscribe);

        await subscription.WaitAsync(cts.Token);

        var single = Assert.Single(received);
        Assert.Same(afterSubscribe, single);
    }

    [Fact]
    public async Task Cancelled_subscription_stops_receiving_events()
    {
        var broadcaster = new ModelLifecycleEventBroadcaster(NullLogger<ModelLifecycleEventBroadcaster>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var subscriberCts = new CancellationTokenSource();

        var received = new List<ModelLifecycleEvent>();
        var subscription = Task.Run(async () =>
        {
            await foreach (var modelEvent in broadcaster.Subscribe(subscriberCts.Token))
            {
                received.Add(modelEvent);
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token);
        broadcaster.Publish(new ModelLifecycleEvent("model.status", "{\"status\":\"queued\"}", DateTimeOffset.UtcNow));

        await WaitForAsync(() => received.Count == 1, cts.Token);
        await subscriberCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.WaitAsync(cts.Token));

        // Publishing after cancellation must not throw and must not reach the cancelled stream.
        broadcaster.Publish(new ModelLifecycleEvent("model.status", "{\"status\":\"ready\"}", DateTimeOffset.UtcNow));
        Assert.Single(received);
    }

    [Fact]
    public async Task Disposed_subscription_is_removed_from_the_broadcaster()
    {
        var broadcaster = new ModelLifecycleEventBroadcaster(NullLogger<ModelLifecycleEventBroadcaster>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var subscriberCts = new CancellationTokenSource();

        var subscription = Task.Run(async () =>
        {
            await foreach (var _ in broadcaster.Subscribe(subscriberCts.Token))
            {
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token);
        await subscriberCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.WaitAsync(cts.Token));

        // Subsequent publishes should have no subscribers left; the broadcaster must stay healthy.
        var exception = Record.Exception(() =>
            broadcaster.Publish(new ModelLifecycleEvent("model.status", "{}", DateTimeOffset.UtcNow)));
        Assert.Null(exception);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }
}
