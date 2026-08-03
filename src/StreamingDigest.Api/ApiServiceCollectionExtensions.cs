using Hangfire;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application;
using StreamingDigest.Application.Admin;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.AudioToText;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Infrastructure.Transcripts;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Api;

internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddStreamingDigestApiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ApplicationConfiguration applicationConfiguration,
        string connectionString,
        JobStorage hangfireStorage)
    {
        services.AddOpenApi();
        services.AddHttpClient();
        services.AddHttpClient<MatrixNotificationClient>();
        services.AddSingleton(sp => new MatrixNotificationOptions
        {
            IsEnabled = configuration.GetValue<bool>("notifications:matrix:enabled"),
            HomeserverBaseUrl = configuration["notifications:matrix:homeserverUrl"] ?? "https://matrix-client.matrix.org",
            AccessToken = configuration["notifications:matrix:accessToken"] ?? string.Empty,
            RoomId = configuration["notifications:matrix:roomId"] ?? string.Empty,
            BotUserId = configuration["notifications:matrix:botUserId"],
            OnManualRuns = configuration.GetValue<bool>("notifications:matrix:onManualRuns"),
            OnScheduledRuns = configuration.GetValue<bool>("notifications:matrix:onScheduledRuns"),
            OnBackfillRuns = configuration.GetValue<bool>("notifications:matrix:onBackfillRuns"),
            DashboardBaseUrl = configuration["notifications:matrix:dashboardBaseUrl"] ?? "http://localhost:8080"
        });
        services.AddSingleton<IMatrixNotificationService, MatrixNotificationService>();
        services.AddHangfire(config => config.UseStorage(hangfireStorage));
        services.AddDbContext<StreamingDigestDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IStreamingDigestDbContext>(sp => sp.GetRequiredService<StreamingDigestDbContext>());
        services.AddSingleton<BootstrapAdminUserService>();
        services.AddSingleton<AppAuthService>();
        services.AddSingleton<AppReadinessStateService>();
        services.AddSingleton<FirstUserSetupService>();
        services.AddSingleton<ModelDiscoveryService>();
        services.AddSingleton<IRecentSearchStore>(sp => new PostgresRecentSearchStore(connectionString, sp.GetRequiredService<IEmbeddingService>()));
        services.AddSingleton<ISearchCorpusSearcher>(sp => new PostgresSearchCorpusSearcher(connectionString));
        services.AddSingleton<SearchUiService>(sp => new SearchUiService(
            sp.GetRequiredService<IRecentSearchStore>(),
            sp.GetRequiredService<ISearchCorpusSearcher>(),
            sp.GetRequiredService<IVideoClusterEmbeddingStore>(),
            rankingService: null));
        services.AddSingleton<ISearchDocumentGenerator, SearchDocumentGenerator>();
        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();
        services.AddSingleton<IEffectiveValueService, EffectiveValueService>();
        services.AddSingleton<ISearchDocumentGenerationService, SearchDocumentGenerationService>();
        services.AddTranscriptIngestionPipeline(configuration);
        services.AddScoped<ISearchDocumentEmbeddingStore>(sp => new PostgresSearchDocumentEmbeddingStore(connectionString, sp.GetRequiredService<IEmbeddingService>()));
        services.AddSingleton<IVideoClusterEmbeddingStore>(sp => new PostgresVideoClusterEmbeddingStore(connectionString));
        services.AddScoped<ISearchDocumentRegenerationService, SearchDocumentRegenerationService>();
        services.AddScoped<IAdminOperationStore, EfCoreAdminOperationStore>();
        services.AddScoped<IAdminOperationsService>(sp => new AdminOperationsService(
            applicationConfiguration,
            environment.ContentRootPath,
            sp.GetRequiredService<IAdminOperationStore>(),
            sp.GetService<IEmbeddingService>(),
            sp.GetService<ITranscriptIngestionService>(),
            sp.GetService<ISearchDocumentRegenerationService>()));
        services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
        services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IIngestionRunRepository, IngestionRunRepository>();
        services.AddScoped<IIngestionItemRepository, IngestionItemRepository>();

        return services;
    }
}