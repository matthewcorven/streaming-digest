using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class OllamaEmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingAsync_UsesConfiguredEndpointAndReturnsEmbedding()
    {
        var requests = new List<(string Method, Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!, body));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"custom-model\",\"embedding\":[0.1,0.2,0.3]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = "http://localhost:11434/api/embeddings",
                ["embedding:model"] = "custom-model",
                ["embedding:expectedDimensions"] = "3"
            })
            .Build();

        var service = new OllamaEmbeddingService(httpClient, configuration);

        var result = await service.GenerateEmbeddingAsync("hello world");

        Assert.Single(requests);
        Assert.Equal("POST", requests[0].Method);
        Assert.Equal("http://localhost:11434/api/embeddings", requests[0].Uri.ToString());
        Assert.Contains("hello world", requests[0].Body);
        Assert.Contains("custom-model", requests[0].Body);
        Assert.Equal("custom-model", result.Model);
        Assert.Equal(3, result.Dimensions);
        Assert.Equal([0.1, 0.2, 0.3], result.Values);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_AppendsApiEmbeddingsToAbsoluteBaseUri()
    {
        var requests = new List<(Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            requests.Add((request.RequestUri!, body));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"fallback-model\",\"embedding\":[0.9]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = "https://ollama.example.com"
            })
            .Build();

        var service = new OllamaEmbeddingService(httpClient, configuration);

        var result = await service.GenerateEmbeddingAsync("sample");

        Assert.Single(requests);
        Assert.Equal("https://ollama.example.com/api/embeddings", requests[0].Uri.ToString());
        Assert.Equal("fallback-model", result.Model);
        Assert.Equal(1, result.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SupportsApiEmbedResponseShape()
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"bge-m3\",\"embeddings\":[[0.1,0.2,0.3,0.4]]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = "http://localhost:11434/api/embed",
                ["embedding:expectedDimensions"] = "4"
            })
            .Build();

        var service = new OllamaEmbeddingService(httpClient, configuration);

        var result = await service.GenerateEmbeddingAsync("sample");

        Assert.Single(requests);
        Assert.Equal("http://localhost:11434/api/embed", requests[0].ToString());
        Assert.Equal("bge-m3", result.Model);
        Assert.Equal(4, result.Dimensions);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], result.Values);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_UsesDefaultValuesWhenConfigurationIsMissing()
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"nomic-embed-text\",\"embedding\":[0.1,0.2]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder().Build();
        var service = new OllamaEmbeddingService(httpClient, configuration);

        var result = await service.GenerateEmbeddingAsync("missing config");

        Assert.Single(requests);
        Assert.Equal("http://localhost:11434/api/embeddings", requests[0].ToString());
        Assert.Equal("nomic-embed-text", result.Model);
        Assert.Equal(2, result.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ThrowsWhenExpectedDimensionsDoNotMatch()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"custom-model\",\"embedding\":[0.1,0.2,0.3]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:expectedDimensions"] = "2"
            })
            .Build();

        var service = new OllamaEmbeddingService(httpClient, configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateEmbeddingAsync("boom"));
        Assert.Contains("expected 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ThrowsWhenResponseContainsNoEmbeddingValues()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"custom-model\",\"embedding\":[]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }));

        var configuration = new ConfigurationBuilder().Build();
        var service = new OllamaEmbeddingService(httpClient, configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateEmbeddingAsync("boom"));
        Assert.Contains("no embedding values", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
