namespace StreamingDigest.Application.Configuration;

public sealed class WorkerConcurrencySettings
{
    public const string DefaultChannelConcurrencyKey = "ingestion.defaultConcurrency";
    public const string ScheduledChannelConcurrencyKey = "ingestion.channels.scheduledConcurrency";
    public const string ManualChannelConcurrencyKey = "ingestion.channels.manualConcurrency";
    public const string BackfillChannelConcurrencyKey = "ingestion.channels.backfillConcurrency";
    public const string VideoPerChannelConcurrencyKey = "ingestion.videos.perChannelConcurrency";
    public const string ScreenshotConcurrencyKey = "screenshots.concurrency";
    public const string EmbeddingBatchSizeKey = "embeddings.batchSize";
    public const string EmbeddingWorkerConcurrencyKey = "embeddings.workerConcurrency";
    public const string WebsiteScrapeGlobalConcurrencyKey = "scraping.website.globalConcurrency";
    public const string WebsiteScrapePerHostConcurrencyKey = "scraping.website.perHostConcurrency";
    public const string RepositoryApiGlobalConcurrencyKey = "repositories.api.globalConcurrency";
    public const string RepositoryApiPerHostConcurrencyKey = "repositories.api.perHostConcurrency";
    public const string WhisperGlobalConcurrencyKey = "whisper.globalConcurrency";
    public const string LlmJobGlobalConcurrencyKey = "llm.jobs.globalConcurrency";

    public const int DefaultChannelConcurrencyValue = 1;
    public const int ScheduledChannelConcurrencyValue = 1;
    public const int ManualChannelConcurrencyValue = 1;
    public const int BackfillChannelConcurrencyValue = 1;
    public const int VideoPerChannelConcurrencyValue = 1;
    public const int ScreenshotConcurrencyValue = 1;
    public const int EmbeddingBatchSizeValue = 16;
    public const int EmbeddingWorkerConcurrencyValue = 1;
    public const int WebsiteScrapeGlobalConcurrencyValue = 2;
    public const int WebsiteScrapePerHostConcurrencyValue = 1;
    public const int RepositoryApiGlobalConcurrencyValue = 2;
    public const int RepositoryApiPerHostConcurrencyValue = 1;
    public const int WhisperGlobalConcurrencyValue = 1;
    public const int LlmJobGlobalConcurrencyValue = 1;

    public int DefaultChannelConcurrency { get; set; } = DefaultChannelConcurrencyValue;

    public int ScheduledChannelConcurrency { get; set; } = ScheduledChannelConcurrencyValue;

    public int ManualChannelConcurrency { get; set; } = ManualChannelConcurrencyValue;

    public int BackfillChannelConcurrency { get; set; } = BackfillChannelConcurrencyValue;

    public int VideoPerChannelConcurrency { get; set; } = VideoPerChannelConcurrencyValue;

    public int ScreenshotConcurrency { get; set; } = ScreenshotConcurrencyValue;

    public int EmbeddingBatchSize { get; set; } = EmbeddingBatchSizeValue;

    public int EmbeddingWorkerConcurrency { get; set; } = EmbeddingWorkerConcurrencyValue;

    public int WebsiteScrapeGlobalConcurrency { get; set; } = WebsiteScrapeGlobalConcurrencyValue;

    public int WebsiteScrapePerHostConcurrency { get; set; } = WebsiteScrapePerHostConcurrencyValue;

    public int RepositoryApiGlobalConcurrency { get; set; } = RepositoryApiGlobalConcurrencyValue;

    public int RepositoryApiPerHostConcurrency { get; set; } = RepositoryApiPerHostConcurrencyValue;

    public int WhisperGlobalConcurrency { get; set; } = WhisperGlobalConcurrencyValue;

    public int LlmJobGlobalConcurrency { get; set; } = LlmJobGlobalConcurrencyValue;

    public static IReadOnlyDictionary<string, object> CreateSeedDefaults()
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultChannelConcurrencyKey] = DefaultChannelConcurrencyValue,
            [ScheduledChannelConcurrencyKey] = ScheduledChannelConcurrencyValue,
            [ManualChannelConcurrencyKey] = ManualChannelConcurrencyValue,
            [BackfillChannelConcurrencyKey] = BackfillChannelConcurrencyValue,
            [VideoPerChannelConcurrencyKey] = VideoPerChannelConcurrencyValue,
            [ScreenshotConcurrencyKey] = ScreenshotConcurrencyValue,
            [EmbeddingBatchSizeKey] = EmbeddingBatchSizeValue,
            [EmbeddingWorkerConcurrencyKey] = EmbeddingWorkerConcurrencyValue,
            [WebsiteScrapeGlobalConcurrencyKey] = WebsiteScrapeGlobalConcurrencyValue,
            [WebsiteScrapePerHostConcurrencyKey] = WebsiteScrapePerHostConcurrencyValue,
            [RepositoryApiGlobalConcurrencyKey] = RepositoryApiGlobalConcurrencyValue,
            [RepositoryApiPerHostConcurrencyKey] = RepositoryApiPerHostConcurrencyValue,
            [WhisperGlobalConcurrencyKey] = WhisperGlobalConcurrencyValue,
            [LlmJobGlobalConcurrencyKey] = LlmJobGlobalConcurrencyValue
        };
    }
}
