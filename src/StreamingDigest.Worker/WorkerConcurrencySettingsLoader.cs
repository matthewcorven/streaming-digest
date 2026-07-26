using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.Worker;

public static class WorkerConcurrencySettingsLoader
{
    public static async Task LoadAsync(
        string connectionString,
        bool databaseConnected,
        ILogger logger,
        WorkerConcurrencySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        if (!databaseConnected || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            settings.DefaultChannelConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.DefaultChannelConcurrencyKey, WorkerConcurrencySettings.DefaultChannelConcurrencyValue, logger, cancellationToken);
            settings.ScheduledChannelConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.ScheduledChannelConcurrencyKey, settings.DefaultChannelConcurrency, logger, cancellationToken);
            settings.ManualChannelConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.ManualChannelConcurrencyKey, settings.DefaultChannelConcurrency, logger, cancellationToken);
            settings.BackfillChannelConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.BackfillChannelConcurrencyKey, settings.DefaultChannelConcurrency, logger, cancellationToken);
            settings.VideoPerChannelConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.VideoPerChannelConcurrencyKey, WorkerConcurrencySettings.VideoPerChannelConcurrencyValue, logger, cancellationToken);
            settings.ScreenshotConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.ScreenshotConcurrencyKey, WorkerConcurrencySettings.ScreenshotConcurrencyValue, logger, cancellationToken);
            settings.EmbeddingBatchSize = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.EmbeddingBatchSizeKey, WorkerConcurrencySettings.EmbeddingBatchSizeValue, logger, cancellationToken);
            settings.EmbeddingWorkerConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.EmbeddingWorkerConcurrencyKey, WorkerConcurrencySettings.EmbeddingWorkerConcurrencyValue, logger, cancellationToken);
            settings.WebsiteScrapeGlobalConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.WebsiteScrapeGlobalConcurrencyKey, WorkerConcurrencySettings.WebsiteScrapeGlobalConcurrencyValue, logger, cancellationToken);
            settings.WebsiteScrapePerHostConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.WebsiteScrapePerHostConcurrencyKey, WorkerConcurrencySettings.WebsiteScrapePerHostConcurrencyValue, logger, cancellationToken);
            settings.RepositoryApiGlobalConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.RepositoryApiGlobalConcurrencyKey, WorkerConcurrencySettings.RepositoryApiGlobalConcurrencyValue, logger, cancellationToken);
            settings.RepositoryApiPerHostConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.RepositoryApiPerHostConcurrencyKey, WorkerConcurrencySettings.RepositoryApiPerHostConcurrencyValue, logger, cancellationToken);
            settings.WhisperGlobalConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.WhisperGlobalConcurrencyKey, WorkerConcurrencySettings.WhisperGlobalConcurrencyValue, logger, cancellationToken);
            settings.LlmJobGlobalConcurrency = await ReadPositiveIntAsync(connection, WorkerConcurrencySettings.LlmJobGlobalConcurrencyKey, WorkerConcurrencySettings.LlmJobGlobalConcurrencyValue, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load worker concurrency settings; continuing with seeded defaults");
        }
    }

    public static void LogResolvedSettings(ILogger logger, WorkerConcurrencySettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        logger.LogInformation(
            "Resolved worker concurrency settings: DefaultChannels={DefaultChannels}; ScheduledChannels={ScheduledChannels}; ManualChannels={ManualChannels}; BackfillChannels={BackfillChannels}; VideosPerChannel={VideosPerChannel}; Screenshots={Screenshots}; EmbeddingBatchSize={EmbeddingBatchSize}; EmbeddingWorkers={EmbeddingWorkers}; WebsiteGlobal={WebsiteGlobal}; WebsitePerHost={WebsitePerHost}; RepositoryGlobal={RepositoryGlobal}; RepositoryPerHost={RepositoryPerHost}; Whisper={Whisper}; LlmJobs={LlmJobs}",
            settings.DefaultChannelConcurrency,
            settings.ScheduledChannelConcurrency,
            settings.ManualChannelConcurrency,
            settings.BackfillChannelConcurrency,
            settings.VideoPerChannelConcurrency,
            settings.ScreenshotConcurrency,
            settings.EmbeddingBatchSize,
            settings.EmbeddingWorkerConcurrency,
            settings.WebsiteScrapeGlobalConcurrency,
            settings.WebsiteScrapePerHostConcurrency,
            settings.RepositoryApiGlobalConcurrency,
            settings.RepositoryApiPerHostConcurrency,
            settings.WhisperGlobalConcurrency,
            settings.LlmJobGlobalConcurrency);
    }

    private static async Task<int> ReadPositiveIntAsync(
        NpgsqlConnection connection,
        string key,
        int fallback,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var rawJson = await AppSettingReader.ReadJsonAsync(connection, key, cancellationToken);
        if (AppSettingReader.TryParseInt(rawJson, out var value))
        {
            if (value > 0)
            {
                return value;
            }

            logger.LogWarning("App setting {SettingKey} must be greater than zero. Using {FallbackValue} instead.", key, fallback);
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            logger.LogWarning("App setting {SettingKey} must be an integer. Using {FallbackValue} instead.", key, fallback);
        }

        return fallback;
    }
}
