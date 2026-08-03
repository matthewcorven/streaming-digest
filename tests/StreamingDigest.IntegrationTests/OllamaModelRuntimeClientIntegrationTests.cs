using System.Diagnostics;
using System.Net;
using System.Text.Json;
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
/// </summary>
public sealed class OllamaModelRuntimeClientIntegrationTests : IAsyncLifetime
{
    private string? _containerId;
    private string? _volumeName;
    private int _hostPort;
    private bool _dockerAvailable;

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

        // Pull the smallest viable model so /api/tags has something to report. Best-effort.
        await RunDockerAsync($"exec {_containerId} ollama pull qwen2.5:0.5b");
    }

    public Task DisposeAsync() => CleanupAsync();

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
                ["embedding:ollamaEndpoint"] = $"http://localhost:{_hostPort}"
            })
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{_hostPort}") };
        var client = new OllamaModelRuntimeClient(httpClient, configuration);

        var models = await client.ListInstalledModelsAsync();

        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Provider == "ollama" && m.ModelId.StartsWith("qwen2.5", StringComparison.OrdinalIgnoreCase));
    }

    private async Task WaitForOllamaAsync()
    {
        using var httpClient = new HttpClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync($"http://localhost:{_hostPort}/api/tags");
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

        throw new InvalidOperationException($"Ollama container did not become ready on port {_hostPort}.");
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

    private static async Task<(int ExitCode, string Output)> RunDockerAsync(string arguments)
    {
        var psi = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, string.Concat(output, error));
    }

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
