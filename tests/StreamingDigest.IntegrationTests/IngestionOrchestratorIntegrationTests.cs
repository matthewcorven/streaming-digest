using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Application.Orchestration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Application.Screenshots;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using Xunit;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// End-to-end A2 orchestrator test: a stubbed YouTube API adapter feeds one video into
/// <see cref="IngestionOrchestrator"/>, which walks every stage (with stubbed transcript,
/// media-source, scraper, and screenshot dependencies) against a real PostgreSQL database
/// and the real <see cref="EfVideoPipelinePersistence"/> + A1 repositories. Asserts
/// run/item/stage statuses, terminal video status, and persisted pipeline artifacts
/// (transcript, segments, resources, repository record, scraped page, search documents).
/// </summary>
[Collection("PostgreSQL Integration Tests")]
public sealed class IngestionOrchestratorIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-orchestrator-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private DbContextOptions<StreamingDigestDbContext>? _options;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        _options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
    }

    public async Task DisposeAsync() => await StopPostgresContainerAsync();

    [Fact]
    public async Task OneVideoWalksAllStages_andPersistsRunItemAndArtifacts()
    {
        // Arrange: channel + a composition root mirroring the Worker DI wiring.
        var channelId = Guid.NewGuid();
        await using (var seed = new StreamingDigestDbContext(_options!))
        {
            seed.Channels.Add(new Channel
            {
                Id = channelId,
                YoutubeChannelId = "UC_integration_test",
                NameOriginal = "Integration Channel",
                ProfileUrl = "https://www.youtube.com/channel/UC_integration_test",
            });
            await seed.SaveChangesAsync();
        }

        const string youtubeVideoId = "inttestvideo1";
        var (services, _, _) = BuildServiceProvider(youtubeVideoId);

        var orchestrator = services.GetRequiredService<IIngestionOrchestrator>();

        // Act
        var run = await orchestrator.RunChannelIngestionAsync(
            new ChannelIngestionRequest
            {
                ChannelId = channelId,
                RunType = "manual",
                TriggeredBy = "integration-test",
                IsReprocessRequest = false,
                MaxVideoConcurrency = 2,
                OperationId = null,
            },
            CancellationToken.None);

        // Assert: run finalized with counters.
        Assert.Equal("completed", run.Status);
        Assert.Equal(1, run.ChannelsChecked);
        Assert.Equal(1, run.NewVideosFound);
        Assert.Equal(1, run.VideosIngested);
        Assert.Equal(0, run.VideosFailed);
        Assert.Equal(1, run.TranscriptsFound);
        Assert.Equal(1, run.RepositoriesFound);

        await using var db = new StreamingDigestDbContext(_options!);

        // Item reached terminal status with every stage completed.
        var item = await db.IngestionItems.SingleAsync(i => i.IngestionRunId == run.Id);
        Assert.True(item.Status == "processed", $"item.Status={item.Status}; ErrorSummary={item.ErrorSummary}");
        Assert.Equal("completed", item.TranscriptStatus);
        Assert.Equal("completed", item.SegmentsStatus);
        Assert.Equal("completed", item.ScreenshotsStatus);
        Assert.Equal("completed", item.LinksStatus);
        Assert.Equal("completed", item.ReposStatus);
        Assert.Equal("completed", item.WebsitesStatus);
        Assert.Equal("completed", item.EmbeddingsStatus);

        // Video terminal status + run correlation.
        var video = await db.Videos.SingleAsync(v => v.YoutubeVideoId == youtubeVideoId);
        Assert.Equal("processed", video.IngestionStatus);
        Assert.Equal(run.Id, video.LastSuccessfulIngestionRunId);

        // Transcript row seeded by the stub transcript service was persisted.
        var transcript = await db.VideoTranscripts.Include(t => t.Cues).SingleOrDefaultAsync(t => t.VideoId == video.Id);
        Assert.NotNull(transcript);
        Assert.NotEmpty(transcript!.Cues);

        // Pipeline artifacts: segment generation, external resources, repository record.
        // SegmentGeneration.Segments is a read-only nav ignored by EF — load segments via a separate query.
        var generation = await db.SegmentGenerations
            .SingleOrDefaultAsync(g => g.VideoId == video.Id);
        Assert.NotNull(generation);
        var segments = await db.Segments.Where(s => s.SegmentGenerationId == generation!.Id).ToListAsync();
        Assert.NotEmpty(segments);

        // Screenshot rows must survive the pipeline (regression guard for the EF-ignored nav).
        var screenshots = await db.SegmentScreenshots.Where(s => s.VideoId == video.Id).ToListAsync();
        Assert.NotEmpty(screenshots);
        Assert.All(screenshots, s =>
        {
            Assert.NotEqual(Guid.Empty, s.SegmentId);
            Assert.False(string.IsNullOrWhiteSpace(s.FilePath));
        });
        Assert.Equal("completed", video.ScreenshotStatus);

        // Resources are global rows (video linkage is by provenance); assert both kinds exist.
        var resources = await db.ExternalResources.ToListAsync();
        Assert.Contains(resources, r => r.ResourceType == "repository" && r.Domain == "github.com");
        Assert.Contains(resources, r => r.ResourceType == "website" && r.Domain == "example.com");

        var repository = await db.Repositories.SingleOrDefaultAsync(r => r.CanonicalUrl.Contains("github.com"));
        Assert.NotNull(repository);
        Assert.Equal("example", repository!.NormalizedOwner);

        var pages = await db.ScrapedPages.ToListAsync();
        Assert.Contains(pages, p => p.ScrapeStatus == "succeeded" && p.FinalUrl != null && p.FinalUrl.Contains("example.com"));

        // Stubbed embedding store recorded exactly one store call.
        var fake = services.GetRequiredService<RecordingEmbeddingStore>();
        Assert.Equal(1, fake.StoreCallCount);
    }

    // ── Reprocess idempotency: second run must not violate unique constraints ────

    [Fact]
    public async Task RunChannelIngestion_reprocess_upserts_canonical_rows_and_completes_again()
    {
        var channelId = Guid.NewGuid();
        await using (var seed = new StreamingDigestDbContext(_options!))
        {
            seed.Channels.Add(new Channel
            {
                Id = channelId,
                YoutubeChannelId = "UC_reprocess_test",
                NameOriginal = "Reprocess Channel",
                ProfileUrl = "https://www.youtube.com/channel/UC_reprocess_test",
            });
            await seed.SaveChangesAsync();
        }

        const string youtubeVideoId = "r3process001";
        var (services, _, _) = BuildServiceProvider(youtubeVideoId);

        var first = await services.GetRequiredService<IIngestionOrchestrator>().RunChannelIngestionAsync(
            new ChannelIngestionRequest { ChannelId = channelId, RunType = "manual", TriggeredBy = "integration-test" });
        var second = await services.GetRequiredService<IIngestionOrchestrator>().RunChannelIngestionAsync(
            new ChannelIngestionRequest { ChannelId = channelId, RunType = "manual", TriggeredBy = "integration-test", IsReprocessRequest = true });

        await using var db = new StreamingDigestDbContext(_options!);
        var runs = await db.IngestionRuns.OrderBy(r => r.CreatedAt).ToListAsync();
        Assert.Equal(2, runs.Count);

        var video = await db.Videos.SingleAsync(v => v.YoutubeVideoId == youtubeVideoId);
        Assert.Equal("processed", video.IngestionStatus);
        Assert.Equal(runs[1].Id, video.LastSuccessfulIngestionRunId);

        // Second run items reach processed — no unique-constraint violation on reprocess.
        var secondRunItems = await db.IngestionItems.Where(i => i.IngestionRunId == runs[1].Id).ToListAsync();
        Assert.NotEmpty(secondRunItems);
        Assert.All(secondRunItems, i => Assert.Equal("processed", i.Status));

        // Canonical rows are upserted, not duplicated.
        Assert.Equal(2, await db.ExternalResources.CountAsync());
        Assert.Single(await db.Repositories.ToListAsync());

        // Scraped pages have no canonical uniqueness — each scrape is a new snapshot row,
        // so a reprocess adds one more page for the same resource (no constraint violation).
        Assert.Equal(2, await db.ScrapedPages.CountAsync());

        // Reprocess creates a new segment generation at the next version (unique on
        // video_id + generation_version), not a collision on version 1.
        var generations = await db.SegmentGenerations
            .Where(g => g.VideoId == video.Id)
            .OrderBy(g => g.GenerationVersion)
            .ToListAsync();
        Assert.Equal(2, generations.Count);
        Assert.Equal(new[] { 1, 2 }, generations.Select(g => g.GenerationVersion).ToArray());
    }

    // ── Composition root ─────────────────────────────────────────────────────────

    private (ServiceProvider Services, LogCapture Capture, StreamingDigestDbContext SeedDb) BuildServiceProvider(string youtubeVideoId)
    {
        var services = new ServiceCollection();

        var capture = new LogCapture();
        services.AddSingleton(capture);
        services.AddLogging(builder => builder.AddProvider(new CaptureLoggerProvider(capture)));
        services.AddSingleton(new ApplicationConfiguration());
        services.AddScoped(_ => new StreamingDigestDbContext(_options!));
        services.AddScoped<IStreamingDigestDbContext>(sp => sp.GetRequiredService<StreamingDigestDbContext>());
        services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IIngestionRunRepository, IngestionRunRepository>();
        services.AddScoped<IIngestionItemRepository, IngestionItemRepository>();
        services.AddScoped<IVideoPipelinePersistence, EfVideoPipelinePersistence>();

        services.AddSingleton<IMetadataAdapterSelector>(_ =>
            new MetadataAdapterSelector(
                new YtDlpMetadataAdapter(),
                new YouTubeApiMetadataAdapter(new HttpClient(new StubYouTubeApiHandler(youtubeVideoId)), "fake-key"),
                new ApplicationConfiguration(),
                NullLogger<MetadataAdapterSelector>.Instance));

        services.AddSingleton<StreamingDigest.Application.Orchestration.IModelReadinessGuard, InterimModelReadinessGuard>();
        services.AddSingleton<IVideoLinkExtractionService, VideoLinkExtractionService>();
        services.AddSingleton<AuthorChapterSegmentationService>();
        services.AddSingleton<IRepositoryMetadataService>(_ =>
            new RepositoryMetadataService(new RepositoryHostDetectionService(), new HttpClient(new StubGitHubApiHandler())));
        services.AddSingleton<ILinkClassificationService>(_ => new LinkClassificationService());
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<DeterministicTranscriptChunkingService>(sp));

        services.AddSingleton<RecordingEmbeddingStore>();
        services.AddSingleton<ISearchDocumentEmbeddingStore>(sp => sp.GetRequiredService<RecordingEmbeddingStore>());
        services.AddSingleton<ISearchDocumentGenerator, SearchDocumentGenerator>();

        // Stage dependencies that would hit the network / filesystem are stubbed:
        services.AddScoped<ITranscriptIngestionService, StubTranscriptIngestionService>();
        services.AddSingleton<IVideoMediaSourceResolver, StubMediaSourceResolver>();
        services.AddSingleton<IWebsiteScraper, StubWebsiteScraper>();
        services.AddSingleton<IScreenshotGenerationService, StubScreenshotGenerationService>();

        services.AddScoped<IVideoStageHandler, TranscriptStageHandler>();
        services.AddScoped<IVideoStageHandler, SegmentsStageHandler>();
        services.AddScoped<IVideoStageHandler, ScreenshotsStageHandler>();
        services.AddScoped<IVideoStageHandler, LinksStageHandler>();
        services.AddScoped<IVideoStageHandler, ReposStageHandler>();
        services.AddScoped<IVideoStageHandler, WebsitesStageHandler>();
        services.AddScoped<IVideoStageHandler, EmbeddingsStageHandler>();
        services.AddScoped<VideoPipelineProcessor>();
        services.AddScoped<IIngestionOrchestrator, IngestionOrchestrator>();

        return (services.BuildServiceProvider(), capture, new StreamingDigestDbContext(_options!));
    }

    // ── Stubs ────────────────────────────────────────────────────────────────────

    /// <summary>Serves a one-video search + video response for the YouTube API adapter.</summary>
    private sealed class StubYouTubeApiHandler(string youtubeVideoId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var json = url.Contains("/search?", StringComparison.Ordinal)
                ? $$$"""{"items":[{"id":{"videoId":"{{{youtubeVideoId}}}"}}]}"""
                : $$$"""
                {"items":[{"id":"{{{youtubeVideoId}}}",
                  "snippet":{"title":"Integration Test Video","description":"Check https://github.com/example/repo and https://example.com/article",
                    "channelTitle":"Integration Channel","publishedAt":"{{{DateTimeOffset.UtcNow:O}}}"},
                  "contentDetails":{"duration":"PT5M"}}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Serves minimal GitHub repo metadata JSON (readme/license/deepwiki fetch
    /// failures degrade gracefully and are ignored).</summary>
    private sealed class StubGitHubApiHandler : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => BuildResponse(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(BuildResponse(request));

        private static HttpResponseMessage BuildResponse(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            if (!url.Contains("api.github.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // readme/license sub-fetches may 404 — the production adapter degrades gracefully.
            if (url.Contains("/readme", StringComparison.Ordinal) || url.Contains("/license", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"full_name":"example/repo","description":"A test repo","stargazers_count":42,"language":"C#","license":{"spdx_id":"MIT"},"default_branch":"main","html_url":"https://github.com/example/repo"}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Seeds a real <see cref="VideoTranscript"/> + cues through the scoped DbContext so
    /// the real <see cref="TranscriptStageHandler"/> can load the transcript row, then
    /// returns its id. Mirrors what the production service does for a caption-backed video.
    /// </summary>
    private sealed class StubTranscriptIngestionService(StreamingDigestDbContext dbContext) : ITranscriptIngestionService
    {
        public async Task<TranscriptIngestionResult> IngestAsync(Guid videoId, CancellationToken cancellationToken)
        {
            // Mirror the real service: deactivate any prior active transcript so the
            // partial unique index on (video_id) WHERE is_active isn't violated on reprocess.
            var priorActive = await dbContext.VideoTranscripts
                .Where(t => t.VideoId == videoId && t.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var prior in priorActive)
            {
                prior.IsActive = false;
            }

            var transcript = new VideoTranscript
            {
                VideoId = videoId,
                SourceType = VideoTranscriptSourceTypes.YouTubeCaption,
                LanguageCode = "en",
                FullTextOriginal = "Hello world, welcome to the integration test. This is the second sentence.",
            };
            transcript.Cues.Add(new TranscriptCue
            {
                TranscriptId = transcript.Id,
                Sequence = 0,
                StartSeconds = 0m,
                EndSeconds = 5m,
                TextOriginal = "Hello world, welcome to the integration test.",
            });
            transcript.Cues.Add(new TranscriptCue
            {
                TranscriptId = transcript.Id,
                Sequence = 1,
                StartSeconds = 5m,
                EndSeconds = 10m,
                TextOriginal = "This is the second sentence.",
            });

            dbContext.VideoTranscripts.Add(transcript);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new TranscriptIngestionResult(
                Succeeded: true,
                TranscriptId: transcript.Id,
                SourceType: VideoTranscriptSourceTypes.YouTubeCaption,
                LanguageCode: "en",
                CueCount: 2,
                ErrorMessage: null,
                Skipped: false);
        }
    }

    /// <summary>Resolves a temp file so the screenshots stage generates rather than defers.</summary>
    private sealed class StubMediaSourceResolver : IVideoMediaSourceResolver
    {
        public Task<ResolvedMediaFile?> ResolveAsync(Guid videoId, CancellationToken cancellationToken)
        {
            var path = Path.Combine(Path.GetTempPath(), $"sd-itest-{videoId:N}.mp4");
            File.WriteAllText(path, "stub media");
            return Task.FromResult<ResolvedMediaFile?>(new ResolvedMediaFile(path, DeleteWhenFinished: true));
        }
    }

    private sealed class StubWebsiteScraper : IWebsiteScraper
    {
        public Task<WebsiteScrapeResult> ScrapeFirstPageAsync(WebsiteScrapeRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new WebsiteScrapeResult(
                RequestedUrl: request.Url,
                FinalUrl: request.Url,
                Title: "Example Article",
                Description: "An example website resource",
                OpenGraphJson: null,
                VisibleText: "Example article body text.",
                RobotsAllowed: true,
                HttpStatus: 200,
                ContentType: "text/html",
                ContentHash: "stubhash"));
    }

    private sealed class StubScreenshotGenerationService : IScreenshotGenerationService
    {
        public Task<ScreenshotGenerationResult> GenerateAsync(ScreenshotGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ScreenshotGenerationResult(
                Succeeded: true,
                OutputFilePath: request.OutputFilePath,
                ErrorMessage: null));
    }

    private sealed class RecordingEmbeddingStore : ISearchDocumentEmbeddingStore
    {
        public int StoreCallCount { get; private set; }

        public Task<IReadOnlyList<StoredSearchDocumentEmbedding>> StoreAsync(
            IEnumerable<GeneratedSearchDocument> documents,
            Guid? generatedByOperationId = null,
            CancellationToken cancellationToken = default)
        {
            StoreCallCount++;
            return Task.FromResult<IReadOnlyList<StoredSearchDocumentEmbedding>>([]);
        }

        public Task DeleteForVideoScopeAsync(Guid videoId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteForSourceAsync(string sourceEntityType, Guid sourceEntityId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // ── Container plumbing (mirrors IngestionRunPersistenceIntegrationTests) ─────

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync("docker",
            "run", "--rm", "-d", "--name", _containerName,
            "-e", $"POSTGRES_USER={Username}", "-e", $"POSTGRES_PASSWORD={Password}", "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_hostPort}:5432", ImageName);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the orchestrator integration test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", "stop", _containerName);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                await using var connection = new Npgsql.NpgsqlConnection(_connectionString);
                await connection.OpenAsync(timeoutCts.Token);
                return;
            }
            catch
            {
                await Task.Delay(500, timeoutCts.Token);
            }
        }

        throw new TimeoutException("PostgreSQL container did not become ready within 60 seconds.");
    }

    private static async Task<string> RunProcessAsync(string fileName, params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class LogCapture
    {
        public ConcurrentQueue<string> Entries { get; } = new();
    }

    private sealed class CaptureLoggerProvider(LogCapture capture) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(capture, categoryName);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = $"[{logLevel}] {category}: {formatter(state, exception)}" + (exception is null ? string.Empty : $"\n  EX: {exception}");
            capture.Entries.Enqueue(msg);
        }
    }
}
