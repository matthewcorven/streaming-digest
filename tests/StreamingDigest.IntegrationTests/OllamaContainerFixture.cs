using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Fixture for managing an ephemeral Ollama container during integration tests using Docker.
/// Implements <see cref="IAsyncLifetime"/> for xUnit integration, automatically starting and stopping
/// the container for each test class that uses this fixture.
///
/// The fixture:
/// - Provisions a fresh Docker container running ollama/ollama:latest via `docker run`
/// - Automatically selects a free port for the Ollama HTTP API (11434)
/// - Creates an isolated Docker volume for model cache (/root/.ollama) per test run
/// - Seeds the container with a lightweight model (qwen2.5:0.5b) for repeatable testing
/// - Waits for container readiness via HTTP health check (/api/tags)
/// - Cleans up all resources (container, volume) on disposal
///
/// Usage:
///   public sealed class MyOllamaIntegrationTest : IClassFixture&lt;OllamaContainerFixture&gt;
///   {
///       private readonly OllamaContainerFixture _fixture;
///       public MyOllamaIntegrationTest(OllamaContainerFixture fixture) =&gt; _fixture = fixture;
///
///       [Fact]
///       public async Task TestSomething()
///       {
///           using var http = new HttpClient { BaseAddress = new Uri(_fixture.Endpoint) };
///           var response = await http.GetAsync("/api/tags");
///           Assert.True(response.IsSuccessStatusCode);
///       }
///   }
/// </summary>
public sealed class OllamaContainerFixture : IAsyncLifetime
{
    private const string SeedModel = "qwen2.5:0.5b";
    private static readonly TimeSpan SeedPullTimeout = TimeSpan.FromMinutes(5);

    private string? _containerId;
    private string? _volumeName;
    private int _hostPort;
    private string _endpoint = null!;

    /// <summary>
    /// The HTTP endpoint at which the Ollama API is accessible (e.g., http://localhost:12345).
    /// Only set after <see cref="InitializeAsync"/> completes successfully.
    /// </summary>
    public string Endpoint
    {
        get => _endpoint ?? throw new InvalidOperationException("Fixture not initialized. Did InitializeAsync fail?");
    }

    public IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = Endpoint
            })
            .Build();
    }

    public async Task InitializeAsync()
    {
        Debug.WriteLine($"[OllamaContainerFixture] Initializing: provisioning ephemeral Ollama container");

        try
        {
            _volumeName = $"streamingdigest-it-ollama-{Guid.NewGuid():N}";
            _hostPort = FindFreePort();

            Debug.WriteLine($"[OllamaContainerFixture] Creating container with volume '{_volumeName}' on port {_hostPort}");

            var createArgs = $"container create --name ollama-it-{Guid.NewGuid():N} " +
                             $"-p {_hostPort}:11434 " +
                             $"-v {_volumeName}:/root/.ollama " +
                             "ollama/ollama:latest";

            var (createCode, createOutput) = await RunDockerAsync(createArgs);
            if (createCode != 0)
            {
                Debug.WriteLine($"[OllamaContainerFixture] Container create failed: {createOutput}");
                throw new InvalidOperationException($"Failed to create container: {createOutput}");
            }

            _containerId = createOutput.Trim();
            Debug.WriteLine($"[OllamaContainerFixture] Container created: {_containerId}");

            var (startCode, startOutput) = await RunDockerAsync($"container start {_containerId}");
            if (startCode != 0)
            {
                Debug.WriteLine($"[OllamaContainerFixture] Container start failed: {startOutput}");
                await CleanupAsync();
                throw new InvalidOperationException($"Failed to start container: {startOutput}");
            }

            _endpoint = $"http://localhost:{_hostPort}";
            Debug.WriteLine($"[OllamaContainerFixture] Container started; Ollama endpoint: {_endpoint}");

            // Wait for container readiness
            Debug.WriteLine($"[OllamaContainerFixture] Waiting for Ollama API to be ready");
            await WaitForOllamaAsync();
            Debug.WriteLine($"[OllamaContainerFixture] Ollama API is ready");

            // Seed the model
            Debug.WriteLine($"[OllamaContainerFixture] Seeding model '{SeedModel}' (timeout: {SeedPullTimeout.TotalMinutes:F0} min)");
            using var seedCts = new CancellationTokenSource(SeedPullTimeout);
            var (seedCode, seedErr) = await RunDockerAsync($"exec {_containerId} ollama pull {SeedModel}", seedCts.Token);
            if (seedCode != 0)
            {
                Debug.WriteLine($"[OllamaContainerFixture] Model seed failed: {seedErr}");
                await CleanupAsync();
                throw new InvalidOperationException(
                    $"Seed `ollama pull {SeedModel}` failed (exit {seedCode}) within {SeedPullTimeout.TotalMinutes:F0} min. stderr: {seedErr}");
            }

            Debug.WriteLine($"[OllamaContainerFixture] Model '{SeedModel}' seeded successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OllamaContainerFixture] Initialization failed: {ex.Message}");
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        Debug.WriteLine($"[OllamaContainerFixture] Stopping container and cleaning up");
        await CleanupAsync();
    }

    /// <summary>
    /// Waits for the Ollama HTTP API to become responsive by polling the /api/tags endpoint.
    /// </summary>
    private async Task WaitForOllamaAsync()
    {
        using var probe = new HttpClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await probe.GetAsync($"{Endpoint}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[OllamaContainerFixture] Ollama readiness check succeeded on attempt {attempt + 1}");
                    return;
                }

                Debug.WriteLine($"[OllamaContainerFixture] Readiness check attempt {attempt + 1} returned {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[OllamaContainerFixture] Readiness check attempt {attempt + 1} failed: {ex.Message}");
            }

            await Task.Delay(1000);
        }

        throw new InvalidOperationException($"Ollama container did not become ready at {Endpoint}.");
    }

    /// <summary>
    /// Runs a Docker CLI command and returns the exit code and combined output.
    /// </summary>
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

    /// <summary>
    /// Finds a free TCP port on localhost.
    /// </summary>
    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Cleans up the Docker container and volume.
    /// </summary>
    private async Task CleanupAsync()
    {
        if (_containerId is not null)
        {
            Debug.WriteLine($"[OllamaContainerFixture] Removing container {_containerId}");
            await RunDockerAsync($"container rm -f {_containerId}");
            _containerId = null;
        }

        if (_volumeName is not null)
        {
            Debug.WriteLine($"[OllamaContainerFixture] Removing volume {_volumeName}");
            await RunDockerAsync($"volume rm {_volumeName}");
            _volumeName = null;
        }
    }
}
