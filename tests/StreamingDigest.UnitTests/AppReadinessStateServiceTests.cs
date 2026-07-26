using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class AppReadinessStateServiceTests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-readiness-tests-{Guid.NewGuid():N}";
    private readonly int _hostPort = GetAvailablePort();
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        await StartPostgresContainerAsync();
        _connectionString = $"Host=127.0.0.1;Port={_hostPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";
        await WaitForPostgresAsync();
    }

    public async Task DisposeAsync()
    {
        await StopPostgresContainerAsync();
    }

    [Fact]
    public async Task GetStatusAsync_creates_default_steps_and_marks_them_pending()
    {
        var service = new AppReadinessStateService();

        var status = await service.GetStatusAsync(_connectionString!);

        Assert.False(status.IsCoreSetupComplete);
        Assert.False(status.IsFullyReady);
        Assert.Contains(status.Steps, step => step.Key == "embedding_model_verified" && step.Status == "pending");
        Assert.Contains(status.Steps, step => step.Key == "first_channel_added" && step.RequiredForCoreSetup);
    }

    [Fact]
    public async Task VerifyStepAsync_and_complete_core_setup_persist_readiness_state()
    {
        var service = new AppReadinessStateService();

        foreach (var key in new[]
                 {
                     "admin_password_changed",
                     "embedding_model_verified",
                     "llm_model_verified",
                     "audio_to_text_verified",
                     "first_channel_added"
                 })
        {
            var response = await service.VerifyStepAsync(_connectionString!, key, new OnboardingStepVerificationRequest("succeeded", null, new Dictionary<string, object?>
            {
                ["provider"] = "ollama"
            }));

            Assert.Equal("succeeded", response.Step.Status);
            Assert.False(response.IsFullyReady);
        }

        var completion = await service.CompleteCoreSetupAsync(_connectionString!);

        Assert.Equal("succeeded", completion.Status);
        Assert.True(completion.IsCoreSetupComplete);
        Assert.False(completion.IsFullyReady);

        var readiness = await service.GetStatusAsync(_connectionString!);
        var coreSetupStep = Assert.Single(readiness.Steps, step => step.Key == "core_setup_completed");
        Assert.Equal("succeeded", coreSetupStep.Status);

        var fullReadinessStep = await service.VerifyStepAsync(_connectionString!, "schedule_confirmed", new OnboardingStepVerificationRequest("succeeded"));
        Assert.Equal("succeeded", fullReadinessStep.Step.Status);

        var backupResponse = await service.VerifyStepAsync(_connectionString!, "backup_path_verified", new OnboardingStepVerificationRequest("succeeded"));
        Assert.Equal("succeeded", backupResponse.Step.Status);

        var finalReadiness = await service.GetStatusAsync(_connectionString!);
        Assert.True(finalReadiness.IsFullyReady);
    }

    private async Task StartPostgresContainerAsync()
    {
        var dockerArgs = new[]
        {
            "run",
            "--rm",
            "-d",
            "--name",
            _containerName,
            "-e",
            $"POSTGRES_USER={Username}",
            "-e",
            $"POSTGRES_PASSWORD={Password}",
            "-e",
            $"POSTGRES_DB={DatabaseName}",
            "-p",
            $"{_hostPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL readiness test container.");
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        if (string.IsNullOrWhiteSpace(_containerName))
        {
            return;
        }

        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName });
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString!);
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the PostgreSQL readiness test container to become ready.");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout.Trim();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
