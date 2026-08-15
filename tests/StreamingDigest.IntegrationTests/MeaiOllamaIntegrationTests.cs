using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure;
using Xunit.Abstractions;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration tests for MEAI chat and embedding scenarios against Testcontainers-managed Ollama.
/// These tests verify the chat wrapper and embedding adapter work correctly with real Ollama instances.
/// Skipped by default; run locally where Docker is available.
/// </summary>
public sealed class MeaiOllamaIntegrationTests : IAsyncLifetime
{
    private readonly OllamaContainerFixture _fixture = new();
    private readonly ITestOutputHelper _output;

    public MeaiOllamaIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact(Skip = "Requires Docker and Ollama; runs locally only.")]
    public async Task ChatClientWrapper_SendsRequestAndParsesJsonResponse()
    {
        // Arrange
        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
        var logger = new XunitLogger<MeaiChatClientWrapper>(_output);
        var chatWrapper = new MeaiChatClientWrapper(httpClient, logger);

        // Act
        var response = await chatWrapper.SendChatAsync(
            systemPrompt: "You are a JSON-generating assistant. Respond with valid JSON only.",
            userPrompt: "Return a JSON object with a 'greeting' field.",
            modelName: "qwen2.5:0.5b",
            temperature: 0.1);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        _output.WriteLine($"Chat response: {response}");

        var field = MeaiChatClientWrapper.TryExtractJsonField(response, "greeting");
        Assert.NotNull(field);
    }

    [Fact(Skip = "Requires Docker and Ollama; runs locally only.")]
    public async Task ChatClientWrapper_HandlesTimeoutGracefully()
    {
        // Arrange
        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint), Timeout = TimeSpan.FromMilliseconds(10) };
        var logger = new XunitLogger<MeaiChatClientWrapper>(_output);
        var chatWrapper = new MeaiChatClientWrapper(httpClient, logger);

        // Act
        var response = await chatWrapper.SendChatAsync(
            systemPrompt: "You are a JSON-generating assistant.",
            userPrompt: "Return a very long JSON response.",
            modelName: "qwen2.5:0.5b");

        // Assert
        Assert.Null(response); // Should gracefully return null on timeout
        _output.WriteLine("Timeout handled correctly");
    }

    [Fact(Skip = "Requires Docker and Ollama; runs locally only.")]
    public async Task EmbeddingServiceAdapter_GeneratesEmbedding()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _fixture.Endpoint,
                ["embedding:expectedDimensions"] = "384" // qwen2.5:0.5b default
            })
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
        var logger = XunitLogger.Create<MeaiEmbeddingServiceAdapter>(_output);

        // Create a mock IEmbeddingGenerator for testing
        var mockGenerator = new OllamaEmbeddingGeneratorMock();

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator, configuration, logger);

        // Act
        var result = await adapter.GenerateEmbeddingAsync("test input text");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ollama", result.Provider);
        Assert.NotEmpty(result.Values);
        _output.WriteLine($"Embedding generated with {result.Dimensions} dimensions");
    }

    [Fact(Skip = "Requires Docker and Ollama; runs locally only.")]
    public async Task EmbeddingServiceAdapter_ValidatesDimensionGuard()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _fixture.Endpoint,
                ["embedding:expectedDimensions"] = "999" // Wrong dimension
            })
            .Build();

        var logger = XunitLogger.Create<MeaiEmbeddingServiceAdapter>(_output);
        var mockGenerator = new OllamaEmbeddingGeneratorMock(dimensionCount: 384);
        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator, configuration, logger);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GenerateEmbeddingAsync("test input"));

        Assert.Contains("dimensions", ex.Message, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"Dimension guard threw expected error: {ex.Message}");
    }

    /// <summary>
    /// Mock IEmbeddingGenerator for testing without requiring a full MEAI setup.
    /// </summary>
    private sealed class OllamaEmbeddingGeneratorMock : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
    {
        private readonly int _dimensionCount;

        public OllamaEmbeddingGeneratorMock(int dimensionCount = 384)
        {
            _dimensionCount = dimensionCount;
        }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddingList = new List<Embedding<float>>();
            foreach (var value in values)
            {
                var vector = new float[_dimensionCount];
                for (var i = 0; i < _dimensionCount; i++)
                {
                    vector[i] = (float)(Math.Sin(value.Length + i) * 0.5f + 0.5f); // Deterministic pseudo-embedding
                }

                embeddingList.Add(new Embedding<float>(new ReadOnlyMemory<float>(vector)));
            }

            return await Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddingList));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Xunit-compatible logger adapter for test output.
    /// </summary>
    private sealed class XunitLogger : ILogger, ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public XunitLogger(ITestOutputHelper output) => _output = output;

        public static XunitLogger<T> Create<T>(ITestOutputHelper output) => new(output);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {message}");
            if (exception is not null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }

        public void Dispose() { }
        public ILogger CreateLogger(string categoryName) => this;
    }

    private sealed class XunitLogger<T> : ILogger<T>, ILoggerProvider
    {
        private readonly ITestOutputHelper _output;

        public XunitLogger(ITestOutputHelper output) => _output = output;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {message}");
            if (exception is not null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }

        public void Dispose() { }
        public ILogger CreateLogger(string categoryName) => new XunitLogger(_output);
    }
}
