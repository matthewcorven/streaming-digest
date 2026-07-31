namespace StreamingDigest.Web.Services;

public sealed class DelayedBusyIndicator
{
    private readonly Func<CancellationToken, Task> _delay;
    private CancellationTokenSource? _delayCancellation;
    private Task? _pendingDelayTask;

    public DelayedBusyIndicator(TimeSpan? delay = null)
        : this(token => Task.Delay(delay ?? TimeSpan.FromSeconds(1), token))
    {
    }

    public DelayedBusyIndicator(Func<CancellationToken, Task> delay)
    {
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public bool IsVisible { get; private set; }

    public void Start(Func<Task> notifyStateChanged)
    {
        ArgumentNullException.ThrowIfNull(notifyStateChanged);

        CancelPendingDelay();
        IsVisible = false;
        _delayCancellation = new CancellationTokenSource();
        _pendingDelayTask = ShowWhenDelayCompletesAsync(notifyStateChanged, _delayCancellation.Token);
    }

    public async Task StopAsync(Func<Task> notifyStateChanged)
    {
        ArgumentNullException.ThrowIfNull(notifyStateChanged);

        CancelPendingDelay();

        if (_pendingDelayTask is not null)
        {
            try
            {
                await _pendingDelayTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when a slow search finishes before the progress state becomes visible.
            }
            finally
            {
                _pendingDelayTask = null;
            }
        }

        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        await notifyStateChanged();
    }

    private async Task ShowWhenDelayCompletesAsync(Func<Task> notifyStateChanged, CancellationToken cancellationToken)
    {
        await _delay(cancellationToken);
        IsVisible = true;
        await notifyStateChanged();
    }

    private void CancelPendingDelay()
    {
        if (_delayCancellation is null)
        {
            return;
        }

        _delayCancellation.Cancel();
        _delayCancellation.Dispose();
        _delayCancellation = null;
    }
}
