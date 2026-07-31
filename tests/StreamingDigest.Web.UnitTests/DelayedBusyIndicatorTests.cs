using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class DelayedBusyIndicatorTests
{
    [Fact]
    public async Task Delayed_indicator_becomes_visible_after_the_delay_elapses()
    {
        var delaySource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new DelayedBusyIndicator(_ => delaySource.Task);
        var notifications = 0;

        controller.Start(() =>
        {
            notifications++;
            return Task.CompletedTask;
        });

        Assert.False(controller.IsVisible);

        delaySource.SetResult();
        await Task.Yield();

        Assert.True(controller.IsVisible);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Delayed_indicator_stays_hidden_when_work_finishes_before_the_delay()
    {
        var controller = new DelayedBusyIndicator(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var notifications = 0;

        controller.Start(() =>
        {
            notifications++;
            return Task.CompletedTask;
        });

        await controller.StopAsync(() =>
        {
            notifications++;
            return Task.CompletedTask;
        });

        Assert.False(controller.IsVisible);
        Assert.Equal(0, notifications);
    }
}
