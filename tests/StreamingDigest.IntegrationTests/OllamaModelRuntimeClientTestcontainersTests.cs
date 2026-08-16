using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration coverage for <see cref="OllamaModelRuntimeClient"/> using Testcontainers-managed Ollama.
/// This test class is skipped by default — enable it to run locally where Docker is available.
///
/// Run locally: Temporarily remove the <c>Skip</c> attribute, ensure Docker is running,
/// then: <c>dotnet test tests/StreamingDigest.IntegrationTests --filter FullyQualifiedName~OllamaModelRuntimeClientTestcontainersTests</c>
/// </summary>
public sealed class OllamaModelRuntimeClientTestcontainersTests : IAsyncLifetime
{
    private readonly OllamaContainerFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task ListInstalledModelsAsync_ReturnsSeededModelFromContainer()
    {
        var configuration = _fixture.CreateConfiguration();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        var models = await client.ListInstalledModelsAsync();

        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Provider == "ollama" && m.ModelId.StartsWith("qwen2.5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task PullModelAsync_YieldsSuccessForAlreadyLocalModel()
    {
        var configuration = _fixture.CreateConfiguration();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        // The model is already local after seeding, so the pull resolves quickly and emits a
        // terminal "success" status.
        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync("qwen2.5:0.5b"))
        {
            progress.Add(item);
        }

        Assert.NotEmpty(progress);
        Assert.Contains(progress, p => p.Status.Equals("success", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task ShowModelAsync_ReturnsFamiliesNestedUnderDetailsForRealServer()
    {
        var configuration = _fixture.CreateConfiguration();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        var info = await client.ShowModelAsync("qwen2.5:0.5b");

        Assert.Equal("ollama", info.Provider);
        Assert.Equal("qwen2.5:0.5b", info.ModelId);
        // This is the assertion that catches the BLOCKER 1 bug: real Ollama nests families inside
        // details; the parser must read them from there, not the response root.
        Assert.NotEmpty(info.Families);
        Assert.NotNull(info.Details);
    }

    private sealed class PassthroughHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
