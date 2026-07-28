extern alias StreamingDigestWorker;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigestWorker::StreamingDigest.Worker.Scraping;

namespace StreamingDigest.IntegrationTests;

public sealed class ScrapedPagePersistenceIntegrationTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:pg17";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-scraped-page-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;
    private StreamingDigestDbContext? _context;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();

        await new PostgresMigrationRunner(_connectionString).ApplyAsync();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _context = new StreamingDigestDbContext(options);
    }

    public async Task DisposeAsync()
    {
        _context?.Dispose();
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task ScrapeFirstPageAsync_persists_page_data_and_raw_html_debug_path()
    {
        var externalResourceId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var payload = new ScrapeFirstPageResponse(
                "https://www.example.com/article",
                "https://www.example.com/target",
                "Example title",
                "Example description",
                JsonDocument.Parse("{\"og:title\":\"Example title\"}").RootElement,
                "Visible text",
                true,
                200,
                "text/html",
                "sha256:abc123",
                "/tmp/raw-html/debug.html");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://scraper.internal")
        };

        var client = new ScraperClient(
            httpClient,
            new NoOpScrapeFailureRecorder(),
            new WorkerOperationConcurrencyController(new WorkerConcurrencySettings()),
            new ApplicationConfiguration(),
            _context);

        var response = await client.ScrapeFirstPageAsync(new ScrapeFirstPageRequest("https://www.example.com/article", externalResourceId, RespectRobotsTxt: true, DebugCaptureRawHtml: true));

        Assert.Equal("https://www.example.com/target", response.FinalUrl);
        Assert.Equal("/tmp/raw-html/debug.html", response.RawHtmlDebugPath);

        await using var connection = new NpgsqlConnection(_connectionString!);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT external_resource_id, final_url, title_original, description_original, visible_text_original, robots_allowed, scrape_status, raw_html_debug_path, content_hash, http_status, content_type
            FROM public.scraped_pages
            ORDER BY created_at DESC
            LIMIT 1;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(externalResourceId, reader.GetGuid(0));
        Assert.Equal("https://www.example.com/target", reader.GetString(1));
        Assert.Equal("Example title", reader.GetString(2));
        Assert.Equal("Example description", reader.GetString(3));
        Assert.Equal("Visible text", reader.GetString(4));
        Assert.True(reader.GetBoolean(5));
        Assert.Equal("succeeded", reader.GetString(6));
        Assert.Equal("/tmp/raw-html/debug.html", reader.GetString(7));
        Assert.Equal("sha256:abc123", reader.GetString(8));
        Assert.Equal(200, reader.GetInt32(9));
        Assert.Equal("text/html", reader.GetString(10));
    }

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync(
            "docker",
            [
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
            ]);

        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the scraped page persistence test container.");
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
            await RunProcessAsync("docker", ["rm", "-f", _containerName]);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL scraped page test container to become ready after 90 seconds.");
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
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class NoOpScrapeFailureRecorder : IScrapeFailureRecorder
    {
        public Task RecordFailureAsync(ScrapeFirstPageRequest request, Exception exception, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
