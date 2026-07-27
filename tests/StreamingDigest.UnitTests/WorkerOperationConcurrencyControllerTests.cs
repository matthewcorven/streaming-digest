using StreamingDigest.Application.Configuration;

namespace StreamingDigest.UnitTests;

public sealed class WorkerOperationConcurrencyControllerTests
{
    // ── Screenshot gate ────────────────────────────────────────────────────────

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
    public async Task RunScreenshotAsync_allows_concurrent_requests_when_limit_is_greater_than_one()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            ScreenshotConcurrency = 2
        });

        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = controller.RunScreenshotAsync(async () =>
        {
            firstEntered.SetResult(true);
            await releaseAll.Task;
        });

        await firstEntered.Task;

        var second = controller.RunScreenshotAsync(async () =>
        {
            secondEntered.SetResult(true);
            await releaseAll.Task;
        });

        // Second operation should enter while first is still holding the gate
        var secondEnteredBeforeRelease = await Task.WhenAny(secondEntered.Task, Task.Delay(500));
        Assert.Same(secondEntered.Task, secondEnteredBeforeRelease);

        releaseAll.SetResult(true);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task RunScreenshotAsync_normalizes_zero_concurrency_limit_to_one_and_serializes()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            ScreenshotConcurrency = 0
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
    public async Task RunScreenshotAsync_releases_gate_when_operation_throws()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            ScreenshotConcurrency = 1
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunScreenshotAsync(Task<object> () => throw new InvalidOperationException("boom")));

        // Gate should be released — second call must not deadlock
        var secondCompleted = false;
        await controller.RunScreenshotAsync(() =>
        {
            secondCompleted = true;
            return Task.CompletedTask;
        });

        Assert.True(secondCompleted);
    }

    [Fact]
    public async Task RunScreenshotAsync_cancels_waiting_operation_when_cancellation_is_requested()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            ScreenshotConcurrency = 1
        });

        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = controller.RunScreenshotAsync(async () =>
        {
            firstEntered.SetResult(true);
            await releaseFirst.Task;
        });

        await firstEntered.Task;

        using var cts = new CancellationTokenSource();
        var second = controller.RunScreenshotAsync(() => Task.CompletedTask, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        releaseFirst.SetResult(true);
        await first;
    }

    // ── Website-scrape gate ────────────────────────────────────────────────────

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

    [Fact]
    public async Task RunWebsiteScrapeAsync_allows_different_hosts_to_proceed_concurrently()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            WebsiteScrapeGlobalConcurrency = 2,
            WebsiteScrapePerHostConcurrency = 1
        });

        var alphaEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var betaEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var alpha = controller.RunWebsiteScrapeAsync("https://alpha.example/page", async () =>
        {
            alphaEntered.SetResult(true);
            await releaseAll.Task;
            return 1;
        });

        var beta = controller.RunWebsiteScrapeAsync("https://beta.example/page", async () =>
        {
            betaEntered.SetResult(true);
            await releaseAll.Task;
            return 2;
        });

        await Task.WhenAll(alphaEntered.Task, betaEntered.Task).WaitAsync(TimeSpan.FromMilliseconds(500));

        releaseAll.SetResult(true);
        var results = await Task.WhenAll(alpha, beta);
        Assert.Equal([1, 2], results);
    }

    [Fact]
    public async Task RunWebsiteScrapeAsync_global_limit_serializes_requests_even_across_different_hosts()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            WebsiteScrapeGlobalConcurrency = 1,
            WebsiteScrapePerHostConcurrency = 1
        });

        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = controller.RunWebsiteScrapeAsync("https://alpha.example/page", async () =>
        {
            firstEntered.SetResult(true);
            await releaseFirst.Task;
            return 1;
        });

        await firstEntered.Task;

        var second = controller.RunWebsiteScrapeAsync("https://beta.example/page", () =>
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

    [Fact]
    public async Task RunWebsiteScrapeAsync_releases_gate_when_operation_throws()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings
        {
            WebsiteScrapeGlobalConcurrency = 1,
            WebsiteScrapePerHostConcurrency = 1
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunWebsiteScrapeAsync(
                "https://example.com/page",
                Task<int> () => throw new InvalidOperationException("boom")));

        // Both gates (global and per-host) must be released
        var secondCompleted = false;
        await controller.RunWebsiteScrapeAsync("https://example.com/page", () =>
        {
            secondCompleted = true;
            return Task.FromResult(0);
        });

        Assert.True(secondCompleted);
    }

    [Fact]
    public async Task RunWebsiteScrapeAsync_throws_for_non_absolute_url()
    {
        var controller = new WorkerOperationConcurrencyController(new WorkerConcurrencySettings());

        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.RunWebsiteScrapeAsync("not-a-url", () => Task.FromResult(0)));
    }
}
