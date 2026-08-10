using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application;
using StreamingDigest.Application.Admin;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure;
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
        // Note: MEAI embedding generator registration disabled due to OllamaSharp 4.0.1 / MEAI 10.5.0 compatibility issues.
        // services.AddMeaiEmbeddingGenerator(configuration);
        // Note: AddMeaiChatClient is commented out because OllamaSharp's IChatClient implementation has compatibility issues with MEAI 10.5.0.
        // Instead, we use MeaiChatClientWrapper which does raw HTTP calls directly.
        // services.AddMeaiChatClient(configuration);
        services.AddMeaiChatClientWrapper(configuration);
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
        services.AddSingleton<IModelRuntimeStateSchemaGuard, ModelRuntimeStateSchemaGuard>();
        services.AddScoped<IModelRuntimeStateRepository>(sp => new PostgresModelRuntimeStateRepository(connectionString));
        services.AddSingleton<IModelReadinessGuard>(sp => new ModelReadinessGuard(new PostgresModelRuntimeStateRepository(connectionString), sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IModelReadinessNotifier, ModelReadinessNotifier>();
        services.AddSingleton<Application.ModelRuntimeReconcileService>();
        services.AddSingleton<FirstUserSetupService>();
        // Verify rewrite (WS-4): the discovery service probes real runtimes (Ollama tags /
        // whisper /health) and persists truth to model_runtime_state + app_readiness_checks.
        // The runtime state repository is stateless (constructed from the connection string),
        // so it is safe to construct directly inside this singleton factory — resolving the
        // scoped IModelRuntimeStateRepository here would fail from the root provider.
        services.AddSingleton<ModelDiscoveryService>(sp => new ModelDiscoveryService(
            sp.GetRequiredService<AppReadinessStateService>(),
            sp.GetService<Application.IModelRuntimeClient>(),
            new PostgresModelRuntimeStateRepository(connectionString),
            sp.GetService<IAudioToTextProvider>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<ModelDiscoveryService>>()));
        services.AddSingleton<IRecentSearchStore>(sp => new PostgresRecentSearchStore(connectionString, sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IModelReadinessGuard>(), sp.GetRequiredService<IModelReadinessNotifier>()));
        services.AddSingleton<ISearchCorpusSearcher>(sp => new PostgresSearchCorpusSearcher(connectionString));
        services.AddSingleton<SearchUiService>(sp => new SearchUiService(
            sp.GetRequiredService<IRecentSearchStore>(),
            sp.GetRequiredService<ISearchCorpusSearcher>(),
            sp.GetRequiredService<IVideoClusterEmbeddingStore>(),
            rankingService: null));
        services.AddSingleton<ISearchDocumentGenerator, SearchDocumentGenerator>();
        // Temporarily use OllamaEmbeddingService due to MEAI 10.5.0 / OllamaSharp 4.0.1 compatibility issues.
        // TODO: Migrate back to MeaiEmbeddingServiceAdapter once compatibility is resolved.
        services.AddSingleton<IEmbeddingService>(sp => new OllamaEmbeddingService(sp.GetRequiredService<HttpClient>(), configuration));
        // The runtime client builds its absolute request URI from config (embedding:ollamaEndpoint,
        // OLLAMA_HOST, ...) rather than from HttpClient.BaseAddress, matching OllamaEmbeddingService.
        // Named client (not a captured singleton HttpClient) so handler rotation refreshes DNS for
        // containerized Ollama; infinite client timeout because model pulls are long-running —
        // cancellation flows via CancellationToken.
        services.AddHttpClient("ollama-runtime")
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
        // Transient + factory-resolved per operation: the named client's pooled handler rotates
        // with SetHandlerLifetime so DNS refreshes for containerized Ollama.
        services.AddTransient<Application.IModelRuntimeClient>(sp =>
            new OllamaModelRuntimeClient(sp.GetRequiredService<IHttpClientFactory>(), configuration));
        services.AddSingleton<IEffectiveValueService, EffectiveValueService>();
        services.AddSingleton<ISearchDocumentGenerationService, SearchDocumentGenerationService>();
        services.AddTranscriptIngestionPipeline(configuration);
        services.AddScoped<ISearchDocumentEmbeddingStore>(sp => new PostgresSearchDocumentEmbeddingStore(connectionString, sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IModelReadinessGuard>(), sp.GetRequiredService<IModelReadinessNotifier>()));
        services.AddSingleton<IVideoClusterEmbeddingStore>(sp => new PostgresVideoClusterEmbeddingStore(connectionString));
        services.AddScoped<ISearchDocumentRegenerationService, SearchDocumentRegenerationService>();
        services.AddScoped<IAdminOperationStore, EfCoreAdminOperationStore>();
        services.AddScoped<IAdminOperationsService>(sp => new AdminOperationsService(
            applicationConfiguration,
            environment.ContentRootPath,
            sp.GetRequiredService<IAdminOperationStore>(),
            sp.GetService<IEmbeddingService>(),
            sp.GetService<ITranscriptIngestionService>(),
            sp.GetService<ISearchDocumentRegenerationService>(),
            sp.GetService<IAudioToTextProvider>(),
            sp.GetService<IModelReadinessGuard>()));
        services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
        services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IIngestionRunRepository, IngestionRunRepository>();
        services.AddScoped<IIngestionItemRepository, IngestionItemRepository>();

        // WS-7 S5/S4 (review Fix 1): register the link-classification and transcript-chunking
        // seams with the shared readiness guard + notifier so the API process never runs them
        // with a null guard and silently degrades. Mirrors the Worker host wiring.
        services.AddScoped<ILinkClassificationService, LinkClassificationService>(sp =>
            new LinkClassificationService(
                sp.GetService<MeaiChatClientWrapper>(),
                sp.GetService<ILogger<LinkClassificationService>>(),
                sp.GetService<IModelReadinessGuard>(),
                sp.GetService<IModelReadinessNotifier>()));
        services.AddScoped(sp => new DeterministicTranscriptChunkingService(
            sp.GetService<MeaiChatClientWrapper>(),
            sp.GetService<ILogger<DeterministicTranscriptChunkingService>>(),
            sp.GetService<IModelReadinessGuard>(),
            sp.GetService<IModelReadinessNotifier>()));

        return services;
    }
}