using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using StreamingDigest.Application;
using StreamingDigest.Infrastructure;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration coverage for <see cref="OllamaModelRuntimeClient"/> against a throwaway Ollama
/// container. Skipped by default — run locally only where Docker is available. Per the plan's
/// Section 10.4 isolation rule, each run mounts a unique per-run Docker volume
/// (<c>streamingdigest-it-ollama-{guid}</c>) at <c>/root/.ollama</c> and deletes it in teardown.
/// The app volume (<c>streamingdigest-ollama-data</c>) is never touched.
///
/// Run locally: temporarily remove the <c>Skip</c> on the test methods, ensure Docker is running
/// and the machine can reach the Ollama model registry, then
/// <c>dotnet test tests/StreamingDigest.IntegrationTests --filter FullyQualifiedName~OllamaModelRuntimeClient</c>.
/// </summary>
public sealed class OllamaModelRuntimeClientIntegrationTests : IAsyncLifetime
{
    private const string SeedModel = "qwen2.5:0.5b";
    private static readonly TimeSpan SeedPullTimeout = TimeSpan.FromMinutes(5);

    private string? _containerId;
    private string? _volumeName;
    private int _hostPort;
    private bool _dockerAvailable;
    private string _endpoint = null!;

    public async Task InitializeAsync()
    {
        _dockerAvailable = await IsDockerAvailableAsync();
        if (!_dockerAvailable)
        {
            return;
        }

        _volumeName = $"streamingdigest-it-ollama-{Guid.NewGuid():N}";
        _hostPort = FindFreePort();

        var createArgs = $"container create --name ollama-it-{Guid.NewGuid():N} " +
                         $"-p {_hostPort}:11434 " +
                         $"-v {_volumeName}:/root/.ollama " +
                         "ollama/ollama:latest";

        var (createCode, createOutput) = await RunDockerAsync(createArgs);
        if (createCode != 0)
        {
            await CleanupAsync();
            _dockerAvailable = false;
            return;
        }

        _containerId = createOutput.Trim();

        var (startCode, _) = await RunDockerAsync($"container start {_containerId}");
        if (startCode != 0)
        {
            await CleanupAsync();
            _dockerAvailable = false;
            return;
        }

        // Seed the smallest viable model with a bounded timeout; surface stderr on failure so a
        // slow network or registry outage doesn't look like an empty /api/tags.
        using var seedCts = new CancellationTokenSource(SeedPullTimeout);
        var (seedCode, seedErr) = await RunDockerAsync($"exec {_containerId} ollama pull {SeedModel}", seedCts.Token);
        if (seedCode != 0)
        {
            await CleanupAsync();
            _dockerAvailable = false;
            throw new InvalidOperationException(
                $"Seed `ollama pull {SeedModel}` failed (exit {seedCode}) within {SeedPullTimeout.TotalMinutes:F0} min. stderr: {seedErr}");
        }

        _endpoint = $"http://localhost:{_hostPort}";
    }

    public Task DisposeAsync() => CleanupAsync();

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task ListInstalledModelsAsync_ReturnsSeededModelFromContainer()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        await WaitForOllamaAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _endpoint
            })
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        var models = await client.ListInstalledModelsAsync();

        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Provider == "ollama" && m.ModelId.StartsWith("qwen2.5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task PullModelAsync_YieldsSuccessForAlreadyLocalModel()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        await WaitForOllamaAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _endpoint
            })
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        // The model is already local after seeding, so the pull resolves quickly and emits a
        // terminal "success" status.
        var progress = new List<ModelPullProgress>();
        await foreach (var item in client.PullModelAsync(SeedModel))
        {
            progress.Add(item);
        }

        Assert.NotEmpty(progress);
        Assert.Contains(progress, p => p.Status.Equals("success", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip = "Requires Docker and a network connection to pull a tiny Ollama model; runs locally only.")]
    public async Task ShowModelAsync_ReturnsFamiliesNestedUnderDetailsForRealServer()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        await WaitForOllamaAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = _endpoint
            })
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var client = new OllamaModelRuntimeClient(new PassthroughHttpClientFactory(httpClient), configuration);

        var info = await client.ShowModelAsync(SeedModel);

        Assert.Equal("ollama", info.Provider);
        Assert.Equal(SeedModel, info.ModelId);
        // This is the assertion that catches the BLOCKER 1 bug: real Ollama nests families inside
        // details; the parser must read them from there, not the response root.
        Assert.NotEmpty(info.Families);
        Assert.NotNull(info.Details);
    }

    private async Task WaitForOllamaAsync()
    {
        using var probe = new HttpClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await probe.GetAsync($"{_endpoint}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Not up yet.
            }

            await Task.Delay(1000);
        }

        throw new InvalidOperationException($"Ollama container did not become ready at {_endpoint}.");
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var (code, _) = await RunDockerAsync("--version");
            return code == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDockerAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, string.Concat(await outputTask, await errorTask));
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task CleanupAsync()
    {
        if (_containerId is not null)
        {
            await RunDockerAsync($"container rm -f {_containerId}");
            _containerId = null;
        }

        if (_volumeName is not null)
        {
            await RunDockerAsync($"volume rm {_volumeName}");
            _volumeName = null;
        }
    }

    private sealed class PassthroughHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
