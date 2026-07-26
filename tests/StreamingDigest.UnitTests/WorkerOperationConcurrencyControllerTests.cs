using StreamingDigest.Application.Configuration;

namespace StreamingDigest.UnitTests;

public sealed class WorkerOperationConcurrencyControllerTests
{
    [Fact]
    public async Task RunScreenshotAsync_serializes_concurrent_requests_when_limit_is_one()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            ScreenshotConcurrency = 1
        });

        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = controller.RunScreenshotAsync(async () =>
        {
            firstEntered.SetResult(true);
            await releaseFirst.Task;
        });

        await firstEntered.Task;

        var second = controller.RunScreenshotAsync(() =>
        {
            secondEntered.SetResult(true);
            return Task.CompletedTask;
        });

        var completedBeforeRelease = await Task.WhenAny(secondEntered.Task, Task.Delay(150));
        Assert.NotSame(secondEntered.Task, completedBeforeRelease);

        releaseFirst.SetResult(true);

        await Task.WhenAll(first, second);
        Assert.True(secondEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task RunWebsiteScrapeAsync_serializes_same_host_requests_when_per_host_limit_is_one()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            WebsiteScrapeGlobalConcurrency = 2,
            WebsiteScrapePerHostConcurrency = 1
        });

        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = controller.RunWebsiteScrapeAsync("https://example.com/one", async () =>
        {
            firstEntered.SetResult(true);
            await releaseFirst.Task;
            return 1;
        });

        await firstEntered.Task;

        var second = controller.RunWebsiteScrapeAsync("https://example.com/two", () =>
        {
            secondEntered.SetResult(true);
            return Task.FromResult(2);
        });

        var completedBeforeRelease = await Task.WhenAny(secondEntered.Task, Task.Delay(150));
        Assert.NotSame(secondEntered.Task, completedBeforeRelease);

        releaseFirst.SetResult(true);

        await Task.WhenAll(first, second);
        Assert.True(secondEntered.Task.IsCompleted);
    }
}
