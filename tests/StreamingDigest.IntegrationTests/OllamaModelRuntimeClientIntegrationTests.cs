using System.Net;
using System.Text.Json;
using StreamingDigest.Application.Models;
using StreamingDigest.Infrastructure.Services;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="OllamaModelRuntimeClient"/> against a throwaway Ollama container.
/// These tests require Docker and are skipped in CI by default.
/// </summary>
public sealed class OllamaModelRuntimeClientIntegrationTests : IAsyncLifetime
{
    private const string OllamaImage = "ollama/ollama:latest";
    private string _containerId = null!;
    private int _hostPort;
    private HttpClient _httpClient = null!;
    private OllamaModelRuntimeClient _client = null!;

    public async Task InitializeAsync()
    {
        // Validate Docker is available
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "docker";
            process.StartInfo.Arguments = "info";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            process.Start();
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Docker is not available or running.");
            }
        }
        catch
        {
            throw new InvalidOperationException("Docker is required for integration tests but is not available.");
        }

        // Start Ollama container
        _hostPort = GetAvailablePort();
        var (containerId, assignedPort) = await StartOllamaContainerAsync(_hostPort);
        _containerId = containerId;
        _hostPort = assignedPort;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_hostPort}"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client = new OllamaModelRuntimeClient(_httpClient);

        // Wait for Ollama to be ready
        await WaitForOllamaReadyAsync(_httpClient);
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();

        if (!string.IsNullOrWhiteSpace(_containerId))
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = $"rm -f {_containerId}";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.Start();
                await process.WaitForExitAsync(cts.Token);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task Tags_ReturnsEmptyList_WhenNoModelsInstalled()
    {
        // Arrange
        var client = new OllamaModelRuntimeClient(_httpClient);

        // Act
        var models = await client.GetInstalledModelsAsync();

        // Assert
        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task ShowModel_ReturnsNull_ForNonExistentModel()
    {
        // Arrange
        var client = new OllamaModelRuntimeClient(_httpClient);

        // Act
        var detail = await client.ShowModelAsync("nonexistent-model-xyz");

        // Assert
        Assert.Null(detail);
    }

    private static async Task<(string ContainerId, int Port)> StartOllamaContainerAsync(int requestedPort)
    {
        // Pull the image first (if not already cached)
        using (var pullCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        using (var pullProcess = new System.Diagnostics.Process())
        {
            pullProcess.StartInfo.FileName = "docker";
            pullProcess.StartInfo.Arguments = $"pull {OllamaImage}";
            pullProcess.StartInfo.RedirectStandardOutput = true;
            pullProcess.StartInfo.RedirectStandardError = true;
            pullProcess.Start();
            await pullProcess.WaitForExitAsync(pullCts.Token);
        }

        // Run the container
        using (var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        using (var runProcess = new System.Diagnostics.Process())
        {
            runProcess.StartInfo.FileName = "docker";
            runProcess.StartInfo.Arguments = $"run -d --rm -p {requestedPort}:11434 {OllamaImage}";
            runProcess.StartInfo.RedirectStandardOutput = true;
            runProcess.StartInfo.RedirectStandardError = true;
            runProcess.Start();
            await runProcess.WaitForExitAsync(runCts.Token);

            var containerId = (await runProcess.StandardOutput.ReadToEndAsync()).Trim();
            if (string.IsNullOrWhiteSpace(containerId))
            {
                var error = await runProcess.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Failed to start Ollama container: {error}");
            }

            // Get the actual host port
            using (var portCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var portProcess = new System.Diagnostics.Process())
            {
                portProcess.StartInfo.FileName = "docker";
                portProcess.StartInfo.Arguments = $"port {containerId} 11434/tcp";
                portProcess.StartInfo.RedirectStandardOutput = true;
                portProcess.StartInfo.RedirectStandardError = true;
                portProcess.Start();
                await portProcess.WaitForExitAsync(portCts.Token);

                var portInfo = (await portProcess.StandardOutput.ReadToEndAsync()).Trim();
                var actualPort = requestedPort; // default to requested
                if (!string.IsNullOrWhiteSpace(portInfo))
                {
                    // Docker port output format: "0.0.0.0:49153"
                    var parts = portInfo.Split(":");
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var parsedPort))
                    {
                        actualPort = parsedPort;
                    }
                }

                return (containerId, actualPort);
            }
        }
    }

    private static async Task WaitForOllamaReadyAsync(HttpClient client)
    {
        var maxRetries = 30;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                // Ollama returns 200 on /api/tags (or empty blob) when ready
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await client.GetAsync("/", cts.Token);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Response on / may be 404, that's fine — the server is listening
                    return;
                }
            }
            catch
            {
                // Not ready yet; wait and retry
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException("Ollama container did not become ready within the expected time.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Helper to run a Docker command and return the output.
    /// </summary>
    private static async Task<string> RunDockerCommandAsync(string arguments, int timeoutSeconds = 30)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "docker";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        await process.WaitForExitAsync(cts.Token);
        return await process.StandardOutput.ReadToEndAsync();
    }
}