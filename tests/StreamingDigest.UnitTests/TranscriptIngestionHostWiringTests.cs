using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StreamingDigest.Application;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Infrastructure.Transcripts;

namespace StreamingDigest.UnitTests;

public sealed class TranscriptIngestionHostWiringTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly RecordingSearchDocumentEmbeddingStore _embeddingStore = new();
    private readonly RecordingAudioToTextProvider _audioToTextProvider = new();
    private readonly RecordingTemporaryMediaManager _temporaryMediaManager = new();

    public TranscriptIngestionHostWiringTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["whisper:baseUrl"] = "http://whisper.test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<StreamingDigestDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IStreamingDigestDbContext>(sp => sp.GetRequiredService<StreamingDigestDbContext>());
        services.AddSingleton<ISearchDocumentGenerator, SearchDocumentGenerator>();
        services.AddScoped<ISearchDocumentEmbeddingStore>(_ => _embeddingStore);
        services.AddTranscriptIngestionPipeline(configuration);
        services.RemoveAll<IAudioToTextProvider>();
        services.RemoveAll<ITemporaryMediaManager>();
        services.RemoveAll<IVideoMediaSourceResolver>();
        services.AddScoped<IAudioToTextProvider>(_ => _audioToTextProvider);
        services.AddScoped<ITemporaryMediaManager>(_ => _temporaryMediaManager);
        services.AddScoped<IVideoMediaSourceResolver>(_ => new StubVideoMediaSourceResolver("/tmp/downloaded-media.m4a", deleteWhenFinished: true));

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StreamingDigestDbContext>();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddTranscriptIngestionPipeline_AllowsHostResolvedServiceToReachWhisperFallbackAndEmbeddingStorage()
    {
        Guid videoId;

        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StreamingDigestDbContext>();
            var video = await SeedVideoAsync(context, "host-wiring-video");
            video.TitleOverride = "Resolved through DI";
            video.DescriptionOriginal = "Host wiring regression coverage";

            var activeGeneration = new SegmentGeneration
            {
                VideoId = video.Id,
                SourceType = SegmentSourceTypes.DeterministicChunk,
                GenerationVersion = 1,
                IsActive = true,
                RequiresUserApproval = false,
                Status = "active"
            };

            context.SegmentGenerations.Add(activeGeneration);
            context.Segments.Add(new Segment
            {
                VideoId = video.Id,
                SegmentGenerationId = activeGeneration.Id,
                SourceType = SegmentSourceTypes.DeterministicChunk,
                Sequence = 1,
                StartSeconds = 0m,
                EndSeconds = 30m,
                TitleOriginal = "Segment title",
                SummaryOriginal = "Segment summary",
                IsActive = true
            });
            await context.SaveChangesAsync();
            videoId = video.Id;
        }

        TranscriptIngestionResult result;
        using (var scope = _serviceProvider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITranscriptIngestionService>();
            result = await service.IngestAsync(videoId, CancellationToken.None);
        }

        using var verificationScope = _serviceProvider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<StreamingDigestDbContext>();
        var updatedVideo = await verificationContext.Videos.SingleAsync(candidate => candidate.Id == videoId);
        var transcript = await verificationContext.VideoTranscripts.SingleAsync(candidate => candidate.VideoId == videoId);

        Assert.True(result.Succeeded);
        Assert.Equal(VideoTranscriptSourceTypes.LocalWhisper, result.SourceType);
        Assert.Equal("Transcript created through the API host wiring.", transcript.FullTextOriginal);
        Assert.NotNull(updatedVideo.SearchIndexedAt);
        Assert.Equal(["/tmp/downloaded-media.m4a"], _audioToTextProvider.RequestedFilePaths);
        Assert.Empty(_temporaryMediaManager.CreatedPaths);
        Assert.Equal(["/tmp/downloaded-media.m4a"], _temporaryMediaManager.DeletedPaths);

        var storedDocuments = Assert.Single(_embeddingStore.StoredDocumentBatches);
        Assert.Contains(storedDocuments, document => document.DocumentType == SearchDocumentTypeNames.VideoMetadata);
        Assert.Contains(storedDocuments, document => document.DocumentType == SearchDocumentTypeNames.SegmentTitle);
        Assert.Contains(storedDocuments, document => document.DocumentType == SearchDocumentTypeNames.TranscriptChunk);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private static async Task<Video> SeedVideoAsync(StreamingDigestDbContext context, string platformVideoId)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"channel-{platformVideoId}",
            NameOriginal = "Test Channel",
            ProfileUrl = "https://www.youtube.com/@testchannel",
            SourceUrl = "https://www.youtube.com/channel/testchannel"
        };

        var video = new Video(Guid.NewGuid(), "Transcript Host Wiring Test")
        {
            ChannelId = channel.Id,
            PlatformVideoId = platformVideoId,
            YoutubeVideoId = platformVideoId,
            PlatformVideoUrl = $"https://www.youtube.com/watch?v={platformVideoId}",
            VideoUrl = $"https://www.youtube.com/watch?v={platformVideoId}",
            AuthorOriginal = "Test Author"
        };

        context.Channels.Add(channel);
        context.Videos.Add(video);
        await context.SaveChangesAsync();
        return video;
    }

    private sealed class RecordingAudioToTextProvider : IAudioToTextProvider
    {
        public List<string> RequestedFilePaths { get; } = [];

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
        {
            RequestedFilePaths.Add(request.FilePath);
            return Task.FromResult(new AudioTranscriptionResult(
                "whisper.cpp",
                "base.en",
                "en",
                9.1m,
                "Transcript created through the API host wiring.",
                [new AudioTranscriptionCueDto(0m, 1m, "Transcript created through the API host wiring.")]));
        }

        public Task<AudioToTextHealthResult> CheckHealthAsync(CancellationToken ct)
            => Task.FromResult(new AudioToTextHealthResult(true, "whisper.cpp", "http://whisper:8080/", "Test stub reports healthy."));
    }

    private sealed class RecordingTemporaryMediaManager : ITemporaryMediaManager
    {
        public List<string> CreatedPaths { get; } = [];
        public List<string> DeletedPaths { get; } = [];

        public Task<string> CreateTemporaryMediaAsync(string sourceFilePath, CancellationToken cancellationToken)
        {
            var tempPath = "/tmp/copied-media.m4a";
            CreatedPaths.Add(tempPath);
            return Task.FromResult(tempPath);
        }

        public Task DeleteTemporaryMediaAsync(string? filePath, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                DeletedPaths.Add(filePath);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubVideoMediaSourceResolver(string filePath, bool deleteWhenFinished) : IVideoMediaSourceResolver
    {
        public Task<ResolvedMediaFile?> ResolveAsync(Guid videoId, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedMediaFile?>(new ResolvedMediaFile(filePath, deleteWhenFinished));
    }

    private sealed class RecordingSearchDocumentEmbeddingStore : ISearchDocumentEmbeddingStore
    {
        public List<IReadOnlyList<GeneratedSearchDocument>> StoredDocumentBatches { get; } = [];

        public Task DeleteForVideoScopeAsync(Guid videoId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteForSourceAsync(string sourceEntityType, Guid sourceEntityId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<StoredSearchDocumentEmbedding>> StoreAsync(
            IEnumerable<GeneratedSearchDocument> documents,
            Guid? generatedByOperationId = null,
            CancellationToken cancellationToken = default)
        {
            var batch = documents.ToArray();
            StoredDocumentBatches.Add(batch);
            return Task.FromResult<IReadOnlyList<StoredSearchDocumentEmbedding>>([]);
        }
    }
}
