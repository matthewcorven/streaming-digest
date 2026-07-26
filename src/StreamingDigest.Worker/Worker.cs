using Microsoft.Extensions.DependencyInjection;
using StreamingDigest.Application.Screenshots;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Worker;

public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IScreenshotGenerationService screenshotGenerationService,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan NotificationDispatchInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetentionCleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inputPath = configuration["screenshots:inputPath"];
        var outputPath = configuration["screenshots:outputPath"];

        if (!string.IsNullOrWhiteSpace(inputPath) && !string.IsNullOrWhiteSpace(outputPath))
        {
            logger.LogInformation("Generating WebP screenshot from {InputPath} to {OutputPath}", inputPath, outputPath);
            var request = new ScreenshotGenerationRequest(
                inputPath!,
                outputPath!,
                configuration.GetValue<double?>("screenshots:offsetSeconds"),
                configuration.GetValue<int?>("screenshots:quality"));

            var result = await screenshotGenerationService.GenerateAsync(request, stoppingToken);
            if (result.Succeeded && result.OutputFilePath is not null)
            {
                logger.LogInformation("Generated WebP screenshot at {OutputPath}", result.OutputFilePath);
            }
            else
            {
                logger.LogWarning("WebP screenshot generation failed: {ErrorMessage}", result.ErrorMessage);
            }
        }

        var nextRetentionCleanupAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var notificationDispatchService = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();
                var dispatchedMessages = await notificationDispatchService.DispatchPendingAsync(stoppingToken);
                if (dispatchedMessages.Count > 0 && logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Dispatched {Count} pending outbox messages", dispatchedMessages.Count);
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= nextRetentionCleanupAt)
                {
                    nextRetentionCleanupAt = now.Add(RetentionCleanupInterval);
                    var retentionCleanupService = scope.ServiceProvider.GetRequiredService<IRetentionCleanupService>();
                    var cleanupResult = await retentionCleanupService.RunAsync(stoppingToken);
                    logger.LogInformation(
                        "Retention cleanup run complete. Enabled={RetentionEnabled}; RetentionDays={RetentionDays}; DeletedDomainEvents={DeletedDomainEventCount}",
                        cleanupResult.RetentionEnabled,
                        cleanupResult.RetentionDays,
                        cleanupResult.DeletedDomainEventCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker maintenance loop failed");
            }

            await Task.Delay(NotificationDispatchInterval, stoppingToken);
        }
    }
}
