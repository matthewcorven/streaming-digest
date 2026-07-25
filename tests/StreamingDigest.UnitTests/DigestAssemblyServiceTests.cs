using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class DigestAssemblyServiceTests : IAsyncLifetime
{
    private const string ImageName = "postgres:16-alpine";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-digest-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task Assemble_and_persist_digest_with_transition_and_threshold_rules()
    {
        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString!)
            .Options;

        await using (var context = new StreamingDigestDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            var service = new DigestAssemblyService(context);
            var ingestionRunId = Guid.NewGuid();

            var request = new DigestAssemblyRequest
            {
                IngestionRunId = ingestionRunId,
                RunType = "backfill",
                NewVideos = new[] { new DigestItem { Id = "video-1", Label = "New video" } },
                NewResources = new[] { new DigestResource { Id = "repo-1", Name = "Example repo", ResourceType = "repository" } },
                HighSignalMatches = new[]
                {
                    new HighSignalMatch { Id = "match-1", Label = "High match", SimilarityPercent = 0.91 },
                    new HighSignalMatch { Id = "match-2", Label = "Low match", SimilarityPercent = 0.70 }
                },
                FailedItems = new[] { new DigestItem { Id = "fail-1", Label = "Failed video" } },
                SkippedItems = new[] { new DigestItem { Id = "skip-1", Label = "Skipped video" } },
                ActiveDeferments = new[] { new ActiveDeferment { Id = "defer-1", Label = "Needs review", Reason = "rate limit" } },
                HighSignalThresholdPercent = 0.80,
                IsEmbeddingTransitionActive = true,
                IsBackfillRun = true
            };

            var digest = await service.AssembleAndPersistAsync(request);

            Assert.Equal(ingestionRunId, digest.IngestionRunId);
            Assert.Equal("backfill", digest.RunType);
            var payload = DigestPayloadSerializer.Deserialize(digest.PayloadJson);
            Assert.Single(payload.NewVideos);
            Assert.Single(payload.NewResources);
            Assert.Empty(payload.HighSignalMatches);
            Assert.True(payload.HighSignalEvaluationSkipped);
            Assert.Equal(0.80, payload.HighSignalThresholdPercent);
            Assert.True(payload.IsBackfillRun);
        }
    }

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync("docker", new[]
        {
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-e",
            $"POSTGRES_USER={Username}",
            "-e",
            $"POSTGRES_PASSWORD={Password}",
            "-e",
            $"POSTGRES_DB={DatabaseName}",
            "-p",
            $"{_hostPort}:5432",
            ImageName
        });

        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the digest assembly test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_containerName))
        {
            return;
        }

        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new Npgsql.NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL digest assembly test container to become ready.");
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
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
