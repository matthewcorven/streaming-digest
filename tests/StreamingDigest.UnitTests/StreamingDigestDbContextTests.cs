using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class StreamingDigestDbContextTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:pg17";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-ef-tests-{Guid.NewGuid():N}";
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
    public async Task Can_crud_channels_videos_and_digests()
    {
        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString!)
            .Options;

        var digestRunId = Guid.NewGuid();

        await using (var context = new StreamingDigestDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            var channel = new Channel
            {
                Id = Guid.NewGuid(),
                YoutubeChannelId = "UC123456",
                NameOriginal = "Test Channel",
                ProfileUrl = "HTTPS://YouTube.com/@testchannel/?utm_source=feed",
                SourceUrl = "https://m.youtube.com/c/testchannel?view=videos"
            };

            var video = new Video(Guid.NewGuid(), "Initial title")
            {
                ChannelId = channel.Id,
                PlatformVideoUrl = "https://youtu.be/abc123?si=share",
                PlatformVideoId = "abc123",
                YoutubeVideoId = "abc123",
                AuthorOriginal = "Test author",
                VideoUrl = "https://m.youtube.com/watch?v=abc123&feature=share&utm_campaign=test"
            };

            var digest = new Digest(digestRunId, "standard")
            {
                PayloadJson = "{\"newVideos\":[]}" 
            };

            await context.Channels.AddAsync(channel);
            await context.Videos.AddAsync(video);
            await context.Digests.AddAsync(digest);
            await context.SaveChangesAsync();
        }

        await using (var context = new StreamingDigestDbContext(options))
        {
            var persistedChannel = await context.Channels.SingleAsync(channel => channel.YoutubeChannelId == "UC123456");
            var persistedVideo = await context.Videos.SingleAsync(video => video.PlatformVideoId == "abc123");
            var persistedDigest = await context.Digests.SingleAsync(digest => digest.IngestionRunId == digestRunId);

            Assert.Equal("Test Channel", persistedChannel.NameOriginal);
            Assert.Equal("Initial title", persistedVideo.Title);
            Assert.Equal("standard", persistedDigest.RunType);
            Assert.Equal("https://www.youtube.com/@testchannel", persistedChannel.ProfileUrl);
            Assert.Equal("https://www.youtube.com/channel/UC123456", persistedChannel.SourceUrl);
            Assert.Equal("https://www.youtube.com/watch", persistedVideo.PlatformVideoUrl);
            Assert.Equal("https://www.youtube.com/watch?v=abc123", persistedVideo.VideoUrl);

            persistedChannel.NameOverride = "Updated name";
            persistedChannel.LastIngestionStatus = "success";
            persistedVideo.Title = "Updated title";
            persistedVideo.IngestionStatus = "processed";

            await context.SaveChangesAsync();
        }

        await using (var context = new StreamingDigestDbContext(options))
        {
            var updatedChannel = await context.Channels.SingleAsync(channel => channel.YoutubeChannelId == "UC123456");
            var updatedVideo = await context.Videos.SingleAsync(video => video.PlatformVideoId == "abc123");

            Assert.Equal("Updated name", updatedChannel.NameOverride);
            Assert.Equal("Updated title", updatedVideo.Title);
            Assert.Equal("processed", updatedVideo.IngestionStatus);

            var digestToRemove = await context.Digests.SingleAsync(digest => digest.IngestionRunId == digestRunId);
            context.Videos.Remove(updatedVideo);
            context.Channels.Remove(updatedChannel);
            context.Digests.Remove(digestToRemove);
            await context.SaveChangesAsync();
        }

        await using (var context = new StreamingDigestDbContext(options))
        {
            var channelCount = await context.Channels.CountAsync();
            var videoCount = await context.Videos.CountAsync();

            Assert.Equal(0, channelCount);
            Assert.Equal(0, videoCount);
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL EF test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL EF test container to become ready.");
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
