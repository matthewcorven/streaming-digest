using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Infrastructure.AudioToText;

namespace StreamingDigest.Infrastructure.Transcripts;

public static class TranscriptIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddTranscriptIngestionPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // WS-7 S6 (review Fix 3): construct via ActivatorUtilities so the optional
        // IModelReadinessNotifier (and IModelReadinessGuard) are injected; both hosts
        // already register them as singletons. A direct AddScoped<TImpl>() cannot
        // supply the optional seam dependencies.
        services.AddScoped<ITranscriptIngestionService>(sp =>
            ActivatorUtilities.CreateInstance<TranscriptIngestionService>(sp));
        services.AddScoped<IYouTubeCaptionClient, StubYouTubeCaptionClient>();
        services.AddScoped<ITemporaryMediaManager, TemporaryMediaManager>();
        services.AddScoped<IVideoMediaSourceResolver, YtDlpVideoMediaSourceResolver>();
        services.AddHttpClient<IAudioToTextProvider, LocalWhisperAudioToTextProvider>(client =>
        {
            var whisperBaseUrl = configuration["whisper:baseUrl"]
                ?? Environment.GetEnvironmentVariable("STREAMINGDIGEST_WHISPER_BASE_URL");
            if (!string.IsNullOrWhiteSpace(whisperBaseUrl))
            {
                client.BaseAddress = new Uri(whisperBaseUrl);
            }

            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }
}
