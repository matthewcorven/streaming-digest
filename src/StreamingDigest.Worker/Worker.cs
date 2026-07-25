using StreamingDigest.Application.Screenshots;

namespace StreamingDigest.Worker;

public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IScreenshotGenerationService screenshotGenerationService) : BackgroundService
{
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

        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
