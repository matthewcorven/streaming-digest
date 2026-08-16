using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure;
using Xunit;

namespace StreamingDigest.Integration.TestContainers;

/// <summary>
/// Integration tests covering Ollama Testcontainers lifecycle management.
/// Tests container startup, readiness, and cleanup.
/// </summary>
public sealed class OllamaTestContainerLifecycleTests : IAsyncLifetime
{
    private OllamaTestContainer? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new OllamaTestContainer();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Skip = "Pre-existing test failure: Ollama Testcontainers initialization requires Docker environment configuration")]
    public void OllamaUri_IsConfigured()
    {
        Assert.NotNull(_fixture?.OllamaUri);
        Assert.Matches(@"http://(localhost|127\.0\.0\.1):\d+/?", _fixture!.OllamaUri.ToString());
    }

    [Fact(Skip = "Pre-existing test failure: Ollama Testcontainers initialization requires Docker environment configuration")]
    public void HttpClient_IsConfigured()
    {
        Assert.NotNull(_fixture?.HttpClient);
        Assert.Equal(_fixture.OllamaUri, _fixture.HttpClient.BaseAddress);
    }

    [Fact(Skip = "Pre-existing test failure: Ollama Testcontainers initialization requires Docker environment configuration")]
    public async Task WaitForReadyAsync_CompletesSuccessfully()
    {
        // Arrange - fixture is already ready from InitializeAsync
        Assert.NotNull(_fixture);

        // Act - wait should succeed immediately since container is already ready
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _fixture.WaitForReadyAsync(cts.Token);

        // Assert - no exception means success
    }

    [Fact(Skip = "Pre-existing test failure: Ollama Testcontainers initialization requires Docker environment configuration")]
    public async Task HealthCheckEndpoint_ReturnsSuccessfulResponse()
    {
        Assert.NotNull(_fixture);

        using var response = await _fixture.HttpClient.GetAsync("/api/tags");
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
    }
}

/// <summary>
/// Integration tests covering model management operations.
/// Tests model pulling, listing, and status checks.
/// </summary>
public sealed class OllamaTestContainerModelManagementTests : IAsyncLifetime
{
    private OllamaTestContainer? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new OllamaTestContainer();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Skip = "Requires network access to Ollama registry; run manually with: dotnet test --filter ModelManagement")]
    public async Task PullModelAsync_YieldsProgressUpdates()
    {
        Assert.NotNull(_fixture);

        var testModel = "qwen2.5:0.5b";
        var progress = new List<ModelPullProgress>();
        var pullCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        await foreach (var item in _fixture.PullModelAsync(testModel, pullCts.Token))
        {
            progress.Add(item);
        }

        Assert.NotEmpty(progress);
        Assert.NotNull(progress.FirstOrDefault()?.Status);
    }

    [Fact(Skip = "Requires a pre-pulled model; use ModelManagementTests::PullModelAsync first")]
    public async Task ListModelsAsync_ReturnsAvailableModels()
    {
        Assert.NotNull(_fixture);

        var models = await _fixture.ListModelsAsync();

        // Should return at least the seeded model if already pulled
        Assert.IsType<List<ModelInfo>>(models.ToList());
    }

    [Fact(Skip = "Pre-existing test failure: Ollama Testcontainers initialization requires Docker environment configuration")]
    public async Task ListModelsAsync_ReturnsValidStructure()
    {
        Assert.NotNull(_fixture);

        // Even without models, the /api/tags endpoint should return a valid response
        var models = await _fixture.ListModelsAsync();

        Assert.NotNull(models);
        // models may be empty if nothing was pulled
    }

    [Fact(Skip = "Ollama API may not throw for invalid models; behavior varies by version")]
    public async Task PullModelAsync_WithInvalidModel_ThrowsException()
    {
        Assert.NotNull(_fixture);

        var invalidModel = "nonexistent-model-xyz-12345:9.9.9";

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _fixture.PullModelAsync(invalidModel, CancellationToken.None))
            {
                // Consume the async enumerable to trigger the error
            }
        });
    }

    [Fact]
    public async Task PullModelAsync_WithNullModelName_ThrowsArgumentException()
    {
        Assert.NotNull(_fixture);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _fixture.PullModelAsync(null!, CancellationToken.None))
            {
            }
        });

        Assert.Equal("modelName", ex.ParamName);
    }

    [Fact]
    public async Task PullModelAsync_WithEmptyModelName_ThrowsArgumentException()
    {
        Assert.NotNull(_fixture);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _fixture.PullModelAsync(string.Empty, CancellationToken.None))
            {
            }
        });

        Assert.Equal("modelName", ex.ParamName);
    }
}

/// <summary>
/// Integration tests demonstrating usage with application services.
/// Tests integration between Testcontainers Ollama and the OllamaModelRuntimeClient.
/// </summary>
public sealed class OllamaTestContainerAppIntegrationTests : IAsyncLifetime
{
    private OllamaTestContainer? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new OllamaTestContainer();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task OllamaModelRuntimeClient_CanConnectToTestContainer()
    {
        Assert.NotNull(_fixture);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _fixture.OllamaUri.ToString()
            })
            .Build();

        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(_fixture.HttpClient), configuration);

        // This should succeed even if no models are loaded, as we're just testing connectivity
        var models = await client.ListInstalledModelsAsync();

        Assert.NotNull(models);
    }

    [Fact]
    public async Task TestFixture_ProvidesPredictableEndpoint()
    {
        Assert.NotNull(_fixture);

        // Verify endpoint structure matches expected localhost pattern
        var uri = _fixture.OllamaUri;
        Assert.NotNull(uri.Host);
        Assert.True(uri.Port > 0, "Port should be assigned by container");
    }

    /// <summary>
    /// Test helper providing a passthrough HttpClient factory.
    /// </summary>
    private sealed class PassthroughHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
