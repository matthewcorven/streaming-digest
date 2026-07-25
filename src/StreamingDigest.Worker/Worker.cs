using Microsoft.Extensions.DependencyInjection;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Worker;

public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch pending notification outbox messages");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
