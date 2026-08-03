using System.Net;
using System.Text;
using System.Text.Json;
using StreamingDigest.Application.Models;
using StreamingDigest.Infrastructure.Services;

namespace StreamingDigest.UnitTests;

public sealed class OllamaModelRuntimeClientTests
{
    private const string BaseAddress = "http://localhost:11434";

    [Fact]
    public async Task GetInstalledModelsAsync_ParsesTagsResponse()
    {
        var requests = new List<(string Method, Uri Uri)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            requests.Add((request.Method.Method, request.RequestUri!));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "models": [
                        {
                          "name": "bge-m3",
                          "modified_at": "2024-01-01T00:00:00Z",
                          "size": 123456789,
                          "digest": "sha256:abc123"
                        },
                        {
                          "name": "llama3.1:8b",
                          "modified_at": "2024-02-01T00:00:00Z",
                          "size": 987654321,
                          "digest": "sha256:def456"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var models = await client.GetInstalledModelsAsync();

        Assert.Single(requests);
        Assert.Equal("GET", requests[0].Method);
        Assert.Equal("http://localhost:11434/api/tags", requests[0].Uri.ToString());

        Assert.Equal(2, models.Count);
        Assert.Equal("bge-m3", models[0].Name);
        Assert.Equal(123456789, models[0].SizeBytes);
        Assert.Equal("sha256:abc123", models[0].ModelDigest);
        Assert.Equal(DateTimeOffset.Parse("2024-01-01T00:00:00Z"), models[0].ModifiedAt);

        Assert.Equal("llama3.1:8b", models[1].Name);
        Assert.Equal(987654321, models[1].SizeBytes);
    }

    [Fact]
    public async Task GetInstalledModelsAsync_ReturnsEmptyWhenNoModelsInstalled()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "models": [] }""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var models = await client.GetInstalledModelsAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task GetInstalledModelsAsync_ThrowsOnNonSuccessStatusCode()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInstalledModelsAsync());
    }

    [Fact]
    public async Task PullModelAsync_StreamsProgressLinesAndStopsAtSuccess()
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
                Content = new StringContent(
                    """
                    {"status":"pulling manifest"}
                    {"status":"downloading digest","digest":"sha256:abc123","total":1000,"completed":400}
                    {"status":"downloading digest","digest":"sha256:abc123","total":1000,"completed":1000}
                    {"status":"verifying sha256 digest"}
                    {"status":"writing manifest"}
                    {"status":"success"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var progress = new List<PullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3"))
        {
            progress.Add(item);
        }

        Assert.Single(requests);
        Assert.Equal("POST", requests[0].Method);
        Assert.Equal("http://localhost:11434/api/pull", requests[0].Uri.ToString());
        Assert.Contains("bge-m3", requests[0].Body);

        Assert.Equal(6, progress.Count);
        Assert.Equal("pulling manifest", progress[0].Status);
        Assert.Null(progress[0].Total);
        Assert.Null(progress[0].Completed);

        Assert.Equal("downloading digest", progress[1].Status);
        Assert.Equal("sha256:abc123", progress[1].Digest);
        Assert.Equal(1000, progress[1].Total);
        Assert.Equal(400, progress[1].Completed);

        Assert.Equal(1000, progress[2].Completed);
        Assert.Equal("success", progress[^1].Status);
    }

    [Fact]
    public async Task PullModelAsync_StopsWhenSuccessAppearsBeforeAllLines()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"status":"pulling manifest"}
                    {"status":"success"}
                    {"status":"unexpected-extra"}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var progress = new List<PullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3"))
        {
            progress.Add(item);
        }

        Assert.Equal(2, progress.Count);
        Assert.Equal("pulling manifest", progress[0].Status);
        Assert.Equal("success", progress[1].Status);
    }

    [Fact]
    public async Task PullModelAsync_SkipsBlankLines()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"status":"pulling manifest"}

                    {"status":"success"}

                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var progress = new List<PullProgress>();
        await foreach (var item in client.PullModelAsync("bge-m3"))
        {
            progress.Add(item);
        }

        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public async Task PullModelAsync_ThrowsWhenModelIdIsEmpty()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"success"}""", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in client.PullModelAsync(" "))
            {
            }
        });
    }

    [Fact]
    public async Task ShowModelAsync_ParsesShowResponse()
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
                Content = new StringContent(
                    """
                    {
                      "modelfile": "# Modelfile generated by ollama",
                      "parameters": "temperature 0.7",
                      "template": "{{ .Prompt }}",
                      "details": {
                        "family": "llama",
                        "parameter_size": "8.0B",
                        "quantization_level": "Q4_0"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var detail = await client.ShowModelAsync("llama3.1:8b");

        Assert.Single(requests);
        Assert.Equal("POST", requests[0].Method);
        Assert.Equal("http://localhost:11434/api/show", requests[0].Uri.ToString());
        Assert.Contains("llama3.1:8b", requests[0].Body);

        Assert.NotNull(detail);
        Assert.Contains("Modelfile generated", detail!.Modelfile);
        Assert.Equal("temperature 0.7", detail.Parameters);
        Assert.Equal("{{ .Prompt }}", detail.Template);
        Assert.Contains("llama", detail.DetailsJson);
    }

    [Fact]
    public async Task ShowModelAsync_ReturnsNullWhenModelNotFound()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, cancellationToken) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }))
        {
            BaseAddress = new Uri(BaseAddress),
        };

        var client = new OllamaModelRuntimeClient(httpClient);

        var detail = await client.ShowModelAsync("nonexistent-model");

        Assert.Null(detail);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}