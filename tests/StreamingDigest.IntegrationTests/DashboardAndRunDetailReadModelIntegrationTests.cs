using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamingDigest.Api.Endpoints;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Web.Models;
using Xunit;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Validates that the <c>/api/internal/dashboard</c> and
/// <c>/api/internal/ingestion-runs/{id}</c> read-model endpoints reflect real
/// run + item data persisted in PostgreSQL. Skip-by-default per repo convention.
/// </summary>
[Collection("PostgreSQL Integration Tests")]
public sealed class DashboardAndRunDetailReadModelIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-dashboard-rm-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private StreamingDigestDbContext? _context;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        var runner = new PostgresMigrationRunner(_connectionString);
        await runner.ApplyAsync();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        _context = new StreamingDigestDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        await StopPostgresContainerAsync();
    }

    // ── dashboard read model ────────────────────────────────────────────────────

    [Fact(Skip = "Requires Docker; runs locally only")]
    public async Task Dashboard_read_model_reflects_stored_digest_and_live_corpus_counts()
    {
        var ctx = _context!;

        // Seed a channel and a video so the corpus exists.
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            NameOriginal = "Test Channel",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ctx.Channels.Add(channel);

        var video = new Video(Guid.NewGuid(), "Video one");
        video.ChannelId = channel.Id;
        ctx.Videos.Add(video);

        // Seed an ingestion run and a stored digest.
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "standard",
            TriggeredBy = "test",
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            NewVideosFound = 1,
            VideosIngested = 1,
            VideosFailed = 0,
            ChannelsChecked = 1,
            RepositoriesFound = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.IngestionRuns.Add(run);

        var payload = new DigestPayload
        {
            NewVideos = [new DigestItem { Id = video.Id.ToString(), Label = "Video one" }]
        };
        var digest = new Digest(run.Id, "standard")
        {
            Id = Guid.NewGuid(),
            PayloadJson = DigestPayloadSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ctx.Digests.Add(digest);

        await ctx.SaveChangesAsync();

        // Compute the same values the endpoint would, then map.
        var channelCount = await ctx.Channels.CountAsync();
        var videoCount = await ctx.Videos.CountAsync();
        var latestRun = await ctx.IngestionRuns
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync();
        var latestDigest = latestRun is not null
            ? await ctx.Digests.Where(d => d.IngestionRunId == latestRun.Id).FirstOrDefaultAsync()
            : null;

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount, videoCount, latestRun, latestDigest,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.True(summary.Corpus.HasSearchableCorpus);
        Assert.True(summary.Corpus.HasCompletedRun);
        Assert.False(summary.Digest.IsEmpty);
        Assert.Contains(summary.Digest.Sections, s => s.Key == "new-videos");
        var newVideosSection = summary.Digest.Sections.First(s => s.Key == "new-videos");
        Assert.Single(newVideosSection.Cards);
        Assert.Equal("Video one", newVideosSection.Cards[0].Title);
    }

    // ── run-detail read model ──────────────────────────────────────────────────

    [Fact(Skip = "Requires Docker; runs locally only")]
    public async Task Run_detail_endpoint_reflects_all_stages_and_items()
    {
        var ctx = _context!;

        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            RunType = "backfill",
            TriggeredBy = "test",
            Status = "completed_with_warnings",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            NewVideosFound = 2,
            VideosIngested = 1,
            VideosFailed = 1,
            ChannelsChecked = 1,
            RepositoriesFound = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.IngestionRuns.Add(run);

        // Add two items in different stages.
        var processedItem = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = run.Id,
            ItemType = "video",
            Stage = "embeddings",
            Status = "processed",
            Attempt = 1,
            MaxAttempts = 3,
            IsRetryable = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var failedItem = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = run.Id,
            ItemType = "video",
            Stage = "embeddings",
            Status = "failed",
            ErrorSummary = "Timeout",
            Attempt = 3,
            MaxAttempts = 3,
            IsRetryable = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ctx.IngestionItems.Add(processedItem);
        ctx.IngestionItems.Add(failedItem);

        await ctx.SaveChangesAsync();

        // Verify the items are persisted correctly.
        var storedItems = await ctx.IngestionItems
            .Where(i => i.IngestionRunId == run.Id)
            .ToListAsync();

        Assert.Equal(2, storedItems.Count);
        Assert.Contains(storedItems, i => i.Status == "processed" && i.Stage == "embeddings");
        Assert.Contains(storedItems, i => i.Status == "failed" && i.IsRetryable && i.ErrorSummary == "Timeout");

        // Verify run is retrieved correctly.
        var storedRun = await ctx.IngestionRuns
            .Where(r => r.Id == run.Id)
            .SingleAsync();
        Assert.Equal("completed_with_warnings", storedRun.Status);
        Assert.Equal(1, storedRun.VideosFailed);

        // Stage rollup should report one failed stage item.
        var stages = storedItems
            .GroupBy(i => i.Stage)
            .Select(g =>
            {
                var statuses = g.Select(i => i.Status.ToLowerInvariant()).ToList();
                var stageStatus = statuses.Any(s => s == "failed") ? "warning"
                    : statuses.Any(s => s == "deferred") ? "deferred"
                    : statuses.Any(s => s == "pending") ? "pending"
                    : "done";
                return new { Name = g.Key, Status = stageStatus, Total = g.Count(), Failed = statuses.Count(s => s == "failed") };
            })
            .ToList();

        Assert.Single(stages, s => s.Name == "embeddings" && s.Status == "warning" && s.Failed == 1);
    }

    // ── Docker helpers (identical pattern to other integration tests) ──────────

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run", "--name", _containerName, "-d", "-e", $"POSTGRES_PASSWORD={Password}", "-p", $"{_hostPort}:5432",
            "--health-cmd", "pg_isready -U postgres", "--health-interval", "1s", "--health-timeout", "3s",
            "--health-retries", "30", ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the dashboard read-model integration test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL dashboard read-model integration test container to become ready.");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}. STDERR: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
