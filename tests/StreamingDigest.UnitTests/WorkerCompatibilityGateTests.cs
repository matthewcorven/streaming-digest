using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Screenshots;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Worker;

namespace StreamingDigest.UnitTests;

public sealed class WorkerCompatibilityGateTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotProcessJobs_WhenCompatibilityIsBlocked()
    {
        var compatibility = new UpgradeCompatibilityEvaluation(
            WorkerCanProcessJobs: false,
            Category: UpgradeCategory.ComposeDeploymentMigration,
            RiskLevel: "Manual migration required",
            BackupRecommended: true,
            BackupRequired: false,
            Summary: "Deployment schema version is behind required version.",
            ComposeTag: "v0.8.1-deploy.1.0.0",
            Blockers: ["Deployment schema version is behind required version."]);

        var scopeFactory = new CountingScopeFactory();
        var worker = new Worker.Worker(
            NullLogger<Worker.Worker>.Instance,
            new ConfigurationBuilder().Build(),
            compatibility,
            new WorkerConcurrencySettings(),
            new NoOpScreenshotGenerationService(),
            scopeFactory);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopeFactory.CreateScopeCallCount);
    }

    private sealed class NoOpScreenshotGenerationService : IScreenshotGenerationService
    {
        public Task<ScreenshotGenerationResult> GenerateAsync(ScreenshotGenerationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScreenshotGenerationResult(true, request.OutputFilePath, null));
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            return new EmptyScope();
        }
    }

    private sealed class EmptyScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

        public void Dispose()
        {
        }
    }
}
