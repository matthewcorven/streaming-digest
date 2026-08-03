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
                    {
                        "name": "bge-m3",
                        "model": "bge-m3:latest",
                        "modified_at": "2025-05-10T08:06:48.639712648-07:00",
                        "digest": "sha256:abc",
                        "size": 1195304048,
                        "details": {
                            "family": "bge",
                            "families": ["bge"],
                            "format": "gguf",
                            "parameter_size": "567M",
                            "quantization_level": "F16",
                            "parent_model": ""
                        }
                    },
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
    public async Task ListInstalledModelsAsync_SkipsEntriesWithBlankNames()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(JsonResponse("""
            {
                "models": [
                    { "name": "bge-m3", "digest": "sha256:abc", "size": 1195304048 },
                    { "name": "  ", "digest": "sha256:blank", "size": 1 },
                    { "name": "llama3.1:8b", "digest": "sha256:def", "size": 4661210678 }
                ]
            }
            """))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var result = await client.ListInstalledModelsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("bge-m3", result[0].ModelId);
        Assert.Equal("llama3.1:8b", result[1].ModelId);
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
    public async Task PullModelAsync_StreamsRealisticProgressLinesInOrderWithDerivedPercent()
    {
        var requests = new List<(string Method, Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!, body));
            // Realistic Ollama NDJSON: manifest line (no total/completed), a layer line where
            // completed is absent early, then a layer line with full progress, plus a malformed
            // line mid-stream that must be skipped without aborting, and a terminal success.
            var ndjson = string.Join('\n', new[]
            {
                """{"status":"pulling manifest"}""",
                "{not json",
                """{"status":"pulling sha256:abc","digest":"sha256:abc","total":1000}""",
                """{"status":"downloading","digest":"sha256:abc","total":1000,"completed":250}""",
                """{"status":"downloading","digest":"sha256:abc","total":1000,"completed":1000}""",
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

        // The malformed line is skipped, so six progress events remain (5 status lines + success).
        Assert.Equal(6, progress.Count);

        // pulling manifest — no total/completed → null Percent, still yielded.
        Assert.Equal("pulling manifest", progress[0].Status);
        Assert.Null(progress[0].Total);
        Assert.Null(progress[0].Completed);
        Assert.Null(progress[0].Percent);

        // pulling sha256:abc layer line — total present, completed absent → null Percent.
        Assert.Equal("pulling sha256:abc", progress[1].Status);
        Assert.Equal(1000L, progress[1].Total);
        Assert.Null(progress[1].Completed);
        Assert.Null(progress[1].Percent);

        // downloading 250/1000 → 25%.
        Assert.Equal("downloading", progress[2].Status);
        Assert.Equal(1000L, progress[2].Total);
        Assert.Equal(250L, progress[2].Completed);
        Assert.Equal(25, progress[2].Percent);

        // downloading 1000/1000 → 100%.
        Assert.Equal("downloading", progress[3].Status);
        Assert.Equal(100, progress[3].Percent);

        Assert.Equal("verifying sha256 digest", progress[4].Status);
        Assert.Null(progress[4].Percent);

        Assert.Equal("success", progress[5].Status);
        Assert.Null(progress[5].Percent);
    }

    [Fact]
    public async Task PullModelAsync_TerminalErrorMidStream_ThrowsAndDoesNotYieldErrorLine()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
        {
            var ndjson = string.Join('\n', new[]
            {
                """{"status":"pulling manifest"}""",
                """{"status":"downloading","total":1000,"completed":250}""",
                """{"error":"manifest download failed"}""",
                """{"status":"success"}"""
            }) + "\n";
            return Task.FromResult(StreamResponse(ndjson));
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var progress = new List<ModelPullProgress>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in client.PullModelAsync("nope"))
            {
                progress.Add(item);
            }
        });

        Assert.Contains("manifest download failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        // The error line must not be yielded as a progress event.
        Assert.DoesNotContain(progress, p => p.Status.Contains("error", StringComparison.OrdinalIgnoreCase));
        // Lines before the error were yielded.
        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public async Task PullModelAsync_StreamFalse_YieldsSingleProgressObject()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            // When stream:false Ollama returns a single JSON object (no trailing newline).
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"stream\":false", body, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(JsonResponse("""{"status":"success"}"""));
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3", stream: false))
        {
            progress.Add(item);
        }

        Assert.Single(progress);
        Assert.Equal("success", progress[0].Status);
    }

    [Fact]
    public async Task PullModelAsync_PercentRoundsAwayFromZero()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(StreamResponse(
                """{"status":"downloading","total":3,"completed":1}""" + "\n" +
                """{"status":"success"}""" + "\n"))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync("m"))
        {
            progress.Add(item);
        }

        // 1/3 = 33.33...% → AwayFromZero → 33.
        Assert.Equal(33, progress[0].Percent);
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
    public async Task PullModelAsync_NdjsonLinesSplitAcrossTransportChunks_ParseCorrectly()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
        {
            var ndjson = string.Join('\n', new[]
            {
                """{"status":"pulling manifest"}""",
                """{"status":"downloading","digest":"sha256:abc","total":1000,"completed":250}""",
                """{"status":"success"}"""
            }) + "\n";
            // 7-byte chunks guarantee splits mid-line and mid-UTF8-safe-ASCII token.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ChunkedStreamContent(ndjson, chunkSize: 7)
            });
        }));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3"))
        {
            progress.Add(item);
        }

        Assert.Equal(3, progress.Count);
        Assert.Equal("pulling manifest", progress[0].Status);
        Assert.Equal(25, progress[1].Percent);
        Assert.Equal("success", progress[2].Status);
    }

    [Fact]
    public async Task ShowModelAsync_ParsesDetailsAndFamiliesNestedUnderDetails()
    {
        // Real Ollama wire shape: families and family are nested inside `details`, not at the root.
        var requests = new List<(string Method, Uri Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHttpHandler((request, _) =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            requests.Add((request.Method.Method, request.RequestUri!, body));
            var json = """
            {
                "modelfile": "FROM llama3.1:8b",
                "parameters": "",
                "template": "{{ .Prompt }}",
                "details": {
                    "parent_model": "",
                    "format": "gguf",
                    "family": "llama",
                    "families": ["llama"],
                    "parameter_size": "8B",
                    "quantization_level": "Q4_K_M"
                },
                "model_info": {}
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
    public async Task ShowModelAsync_ParsesMultipleFamiliesNestedUnderDetails()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(JsonResponse("""
            {
                "modelfile": "FROM qwen2.5:7b",
                "details": {
                    "family": "qwen2",
                    "families": ["qwen2", "llama"],
                    "parameter_size": "7B"
                }
            }
            """))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var info = await client.ShowModelAsync("qwen2.5:7b");

        Assert.Equal(["qwen2", "llama"], info.Families);
        Assert.NotNull(info.Details);
    }

    [Fact]
    public async Task ShowModelAsync_NonSuccess_Throws()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("model not found", Encoding.UTF8, "application/json")
            })));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ShowModelAsync("nope"));
        Assert.Contains("404", exception.Message);
    }

    [Fact]
    public async Task ShowModelAsync_DetailsAbsent_GracefulEmptyFamilies()
    {
        using var httpClient = new HttpClient(new StubHttpHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"modelfile":"FROM scratch"}"""))));

        var client = new OllamaModelRuntimeClient(httpClient, new ConfigurationBuilder().Build());

        var info = await client.ShowModelAsync("scratch");

        Assert.Empty(info.Families);
        Assert.Null(info.Details);
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

    /// <summary>Delivers the payload in fixed-size byte chunks, splitting mid-line,
    /// to prove NDJSON parsing is line-framed and not chunk-framed.</summary>
    private sealed class ChunkedStreamContent : HttpContent
    {
        private readonly byte[] _payload;
        private readonly int _chunkSize;

        public ChunkedStreamContent(string payload, int chunkSize)
        {
            _payload = Encoding.UTF8.GetBytes(payload);
            _chunkSize = chunkSize;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (var offset = 0; offset < _payload.Length; offset += _chunkSize)
            {
                var count = Math.Min(_chunkSize, _payload.Length - offset);
                await stream.WriteAsync(_payload.AsMemory(offset, count));
                await stream.FlushAsync();
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _payload.Length;
            return true;
        }
    }
}
