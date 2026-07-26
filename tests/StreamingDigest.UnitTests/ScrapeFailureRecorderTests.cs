using System.Net;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Worker.Scraping;

namespace StreamingDigest.UnitTests;

public sealed class ScrapeFailureRecorderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly StreamingDigestDbContext _context;
    private readonly TestLogger<ScrapeFailureRecorder> _logger;
    private readonly ScrapeFailureRecorder _recorder;

    public ScrapeFailureRecorderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new StreamingDigestDbContext(options);
        _context.Database.EnsureCreated();

        _logger = new TestLogger<ScrapeFailureRecorder>();
        _recorder = new ScrapeFailureRecorder(_context, _logger);
    }

    [Fact]
    public async Task RecordFailureAsync_persists_domain_event_with_summary_and_details()
    {
        var request = new ScrapeFirstPageRequest("https://example.com/failed");
        var exception = new InvalidOperationException("The scraper timed out.");

        await _recorder.RecordFailureAsync(request, exception);

        var domainEvent = await _context.DomainEvents.SingleAsync();
        Assert.Equal(DomainEventTypeCatalog.ScrapeFailed, domainEvent.EventType);
        Assert.Equal("error", domainEvent.Severity);
        Assert.Equal("scrape_request", domainEvent.EntityType);
        Assert.Equal("Scrape failed for https://example.com/failed", domainEvent.Message);
        Assert.Contains("The scraper timed out.", domainEvent.DetailsJson);
        Assert.Contains(_logger.Entries, entry => entry.LogLevel == LogLevel.Error && entry.Message.Contains("Scrape failed for https://example.com/failed"));
    }

    [Fact]
    public async Task ScraperClient_records_domain_event_and_logs_when_scrape_fails()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("scrape unavailable"));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://scraper.internal")
        };
        var client = new ScraperClient(httpClient, _recorder, new WorkerOperationConcurrencyController(new WorkerConcurrencySettings()));
        var request = new ScrapeFirstPageRequest("https://example.com/failed");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ScrapeFirstPageAsync(request));

        var domainEvent = await _context.DomainEvents.SingleAsync();
        Assert.Equal(DomainEventTypeCatalog.ScrapeFailed, domainEvent.EventType);
        Assert.Equal("Scrape failed for https://example.com/failed", domainEvent.Message);
        Assert.Contains(_logger.Entries, entry => entry.LogLevel == LogLevel.Error && entry.Message.Contains("Scrape failed for https://example.com/failed"));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
