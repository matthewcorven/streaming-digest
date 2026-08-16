using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Testcontainers.Ollama;
using Xunit;

namespace StreamingDigest.IntegrationTests;

/// <summary>
/// Reusable fixture for ephemeral Ollama container lifecycle management via Testcontainers.
/// Encapsulates container creation, model seeding, cleanup, and readiness checks.
///
/// Example usage:
/// <code>
/// public class MyOllamaTests : IAsyncLifetime
/// {
///     private readonly OllamaContainerFixture _fixture = new();
///
///     public Task InitializeAsync() => _fixture.InitializeAsync();
///     public Task DisposeAsync() => _fixture.DisposeAsync();
///
///     [Fact]
///     public async Task MyTest()
///     {
///         var config = _fixture.CreateConfiguration();
///         // Use config with OllamaModelRuntimeClient, etc.
///     }
/// }
/// </code>
/// </summary>
public sealed class OllamaContainerFixture : IAsyncLifetime
{
    private const string OllamaImageTag = "0.32.13";
    private const string SeedModel = "qwen2.5:0.5b";
    private static readonly TimeSpan SeedPullTimeout = TimeSpan.FromMinutes(5);

    private OllamaContainer? _container;
    private bool _isReady;

    public string Endpoint => _container?.GetConnectionString() ?? throw new InvalidOperationException("Container not initialized.");

    public async Task InitializeAsync()
    {
        var imageTag = $"ollama/ollama:{OllamaImageTag}";
        _container = new OllamaBuilder()
            .WithImage(imageTag)
            .Build();

        await _container.StartAsync();

        try
        {
            await WaitForOllamaReadyAsync();
            await SeedModelAsync();
            _isReady = true;
        }
        catch
        {
            await _container.StopAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    public IConfiguration CreateConfiguration()
    {
        if (!_isReady)
            throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync() first.");

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:ollamaEndpoint"] = Endpoint
            })
            .Build();
    }

    public IConfiguration CreateConfiguration(string modelId, string endpoint)
    {
        if (!_isReady)
            throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync() first.");

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["embedding:modelId"] = modelId,
                ["embedding:ollamaEndpoint"] = endpoint
            })
            .Build();
    }

    private async Task WaitForOllamaReadyAsync()
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var response = await probe.GetAsync($"{Endpoint}/api/tags");
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

        throw new InvalidOperationException($"Ollama container did not become ready at {Endpoint} within 60 seconds.");
    }

    private async Task SeedModelAsync()
    {
        using var seedCts = new CancellationTokenSource(SeedPullTimeout);
        var containerId = _container!.Id;
        var psi = new ProcessStartInfo("docker", $"exec {containerId} ollama pull {SeedModel}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(seedCts.Token);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(seedCts.Token);
            throw new InvalidOperationException(
                $"Seed `ollama pull {SeedModel}` failed (exit {process.ExitCode}) within {SeedPullTimeout.TotalMinutes:F0} min. stderr: {stderr}");
        }
    }
}
