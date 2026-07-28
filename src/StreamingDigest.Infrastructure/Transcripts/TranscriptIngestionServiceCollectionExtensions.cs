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

        services.AddScoped<ITranscriptIngestionService, TranscriptIngestionService>();
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
