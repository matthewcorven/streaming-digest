using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure;

namespace StreamingDigest.UnitTests;

public sealed class OllamaModelRuntimeClientTests
{
    [Fact]
    public async Task ListInstalledModelsAsync_ParsesTagsIntoPresenceList()
    {
        var requests = new List<(string Method, Uri Uri)>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            requests.Add((request.Method.Method, request.RequestUri!));
            var json = """
            {
                "models": [
                    { "name": "bge-m3", "digest": "sha256:abc", "size": 1195304048 },
                    { "name": "llama3.1:8b", "digest": "sha256:def", "size": 4661210678 }
                ]
            }
            """;
            return Task.FromResult(JsonResponse(json));
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var result = await client.ListInstalledModelsAsync();

        Assert.Single(requests);
        Assert.Equal("GET", requests[0].Method);
        Assert.EndsWith("/api/tags", requests[0].Uri.ToString());
        Assert.Equal(2, result.Count);
        Assert.Equal("ollama", result[0].Provider);
        Assert.Equal("bge-m3", result[0].ModelId);
        Assert.Equal("sha256:abc", result[0].Digest);
        Assert.Equal(1195304048L, result[0].SizeInBytes);
        Assert.Equal("llama3.1:8b", result[1].ModelId);
    }

    [Fact]
    public async Task ListInstalledModelsAsync_EmptyModelsArray_ReturnsEmptyList()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"models":[]}"""))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var result = await client.ListInstalledModelsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListInstalledModelsAsync_NonSuccess_Throws()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListInstalledModelsAsync());
        Assert.Contains("503", exception.Message);
    }

    [Fact]
    public async Task PullModelAsync_StreamsProgressLinesInOrderWithDerivedPercent()
    {
        var requests = new List<(string Method, Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!, body));
            var ndjson = string.Join('\n', new[]
            {
                """{"status":"downloading","total":1000,"completed":250,"digest":"sha256:abc"}""",
                """{"status":"downloading","total":1000,"completed":1000,"digest":"sha256:abc"}""",
                """{"status":"verifying sha256 digest"}""",
                """{"status":"success"}"""
            }) + "\n";
            return Task.FromResult(StreamResponse(ndjson));
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3"))
        {
            progress.Add(item);
        }

        Assert.Single(requests);
        Assert.Equal("POST", requests[0].Method);
        Assert.EndsWith("/api/pull", requests[0].Uri.ToString());
        Assert.Contains("\"stream\":true", requests[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bge-m3", requests[0].Body);

        Assert.Equal(4, progress.Count);
        Assert.Equal("downloading", progress[0].Status);
        Assert.Equal(1000L, progress[0].Total);
        Assert.Equal(250L, progress[0].Completed);
        Assert.Equal(25, progress[0].Percent);

        Assert.Equal("downloading", progress[1].Status);
        Assert.Equal(100, progress[1].Percent);

        Assert.Equal("verifying sha256 digest", progress[2].Status);
        Assert.Null(progress[2].Percent);

        Assert.Equal("success", progress[3].Status);
        Assert.Null(progress[3].Percent);
    }

    [Fact]
    public async Task PullModelAsync_NonSuccess_Throws()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("model not found", Encoding.UTF8, "application/json")
            })));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(client.PullModelAsync("nope")));
        Assert.Contains("404", exception.Message);
    }

    [Fact]
    public async Task ShowModelAsync_ParsesDetailsAndFamilies()
    {
        var requests = new List<(string Method, Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!, body));
            var json = """
            {
                "details": { "family": "llama", "parameter_size": "8B" },
                "modelfile": "FROM llama3.1:8b",
                "families": ["llama"]
            }
            """;
            return Task.FromResult(JsonResponse(json));
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var info = await client.ShowModelAsync("llama3.1:8b");

        Assert.Equal("POST", requests[0].Method);
        Assert.EndsWith("/api/show", requests[0].Uri.ToString());
        Assert.Contains("llama3.1:8b", requests[0].Body);
        Assert.Equal("ollama", info.Provider);
        Assert.Equal("llama3.1:8b", info.ModelId);
        Assert.NotNull(info.Details);
        Assert.Contains("llama", info.Details!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["llama"], info.Families);
    }

    [Fact]
    public async Task EndpointResolvesConfiguredAbsoluteUriWithoutTrailingSlash()
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(JsonResponse("""{"models":[]}"""));
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = "https://ollama.example.com"
            })
            .Build();

        var client = new OllamaModelRuntimeClient(httpClient, configuration);

        await client.ListInstalledModelsAsync();

        Assert.Equal("https://ollama.example.com/api/tags", requests[0].ToString());
    }

    [Fact]
    public async Task EndpointStripsTrailingApiSegment()
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(JsonResponse("""{"models":[]}"""));
        }));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = "http://localhost:11434/api"
            })
            .Build();

        var client = new OllamaModelRuntimeClient(httpClient, configuration);

        await client.ListInstalledModelsAsync();

        Assert.Equal("http://localhost:11434/api/tags", requests[0].ToString());
    }

    private static async Task CollectAsync(IAsyncEnumerable<ModelPullProgress> source)
    {
        await using var enumerator = source.GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync())
        {
            // Drain to surface exceptions from SendAsync.
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage StreamResponse(string ndjson)
        => new(HttpStatusCode.OK) { Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson") };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
