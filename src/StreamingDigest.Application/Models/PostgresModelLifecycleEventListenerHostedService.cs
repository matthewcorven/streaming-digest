using Microsoft.Extensions.Hosting;

namespace StreamingDigest.Application.Models;

/// <summary>
/// Hosted service wrapper for <see cref="PostgresModelLifecycleEventListener"/>. Starts
/// the listener when the host starts and disposes it when the host stops.
/// </summary>
public sealed class PostgresModelLifecycleEventListenerHostedService : BackgroundService
{
    private readonly PostgresModelLifecycleEventListener _listener;

    public PostgresModelLifecycleEventListenerHostedService(PostgresModelLifecycleEventListener listener)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.StartListening();
        // Return a task that completes when stoppingToken is cancelled.
        return Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async ValueTask DisposeAsync()
    {
        await _listener.DisposeAsync();
        await base.DisposeAsync();
    }
}
