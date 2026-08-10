using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;

namespace StreamingDigest.E2E;

/// <summary>
/// WS-5 E2E slice (plan §9.3/§10): real Ollama container with a unique per-run volume,
/// real Postgres, real API + worker processes. Downloads a small model end to end and
/// asserts it becomes present in /api/tags, the operation completes, and verify reports
/// verified:true. Opt-in gated: set STREAMINGDIGEST_E2E_REAL_OLLAMA=1 to run.
/// </summary>
public sealed class ModelDownloadE2ETests : IAsyncLifetime
{
    private const string PostgresImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string OllamaImageName = "ollama/ollama:latest";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";
    private const string DownloadModelId = "bge-m3";

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string _postgresContainerName;
    private readonly string _ollamaContainerName;
    private readonly string _ollamaVolumeName;
    private readonly int _postgresPort = GetAvailablePort();
    private readonly int _ollamaPort = GetAvailablePort();
    private readonly int _apiPort = GetAvailablePort();
    private readonly string _repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private Process? _apiProcess;
    private Process? _workerProcess;
    private string? _connectionString;
    private string ApiBaseUrl => $"http://127.0.0.1:{_apiPort}";
    private string OllamaBaseUrl => $"http://127.0.0.1:{_ollamaPort}";

    public ModelDownloadE2ETests()
    {
        _postgresContainerName = $"streaming-digest-modeldl-e2e-pg-{_runId}";
        _ollamaContainerName = $"streaming-digest-modeldl-e2e-ollama-{_runId}";
        // Plan §10: unique per-run volume, never reuse streamingdigest-ollama-data.
        _ollamaVolumeName = $"streamingdigest-it-ollama-{_runId}";
    }

    private static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("STREAMINGDIGEST_E2E_REAL_OLLAMA"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("STREAMINGDIGEST_E2E_REAL_OLLAMA"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        _connectionString = $"Host=127.0.0.1;Port={_postgresPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";

        await StartPostgresContainerAsync();
        await StartOllamaContainerAsync();
        await WaitForPostgresAsync();
        await WaitForHttpAsync($"{OllamaBaseUrl}/api/tags", TimeSpan.FromSeconds(60));
        await StartApiAsync();
        await StartWorkerAsync();
    }

    public async Task DisposeAsync()
    {
        await StopProcessAsync(_apiProcess);
        _apiProcess = null;
        await StopProcessAsync(_workerProcess);
        _workerProcess = null;

        await TryDockerAsync("rm", "-f", _postgresContainerName);
        await TryDockerAsync("rm", "-f", _ollamaContainerName);
        // Plan §10: the unique per-run volume is removed in teardown.
        await TryDockerAsync("volume", "rm", "-f", _ollamaVolumeName);
    }

    [Fact]
    public async Task Download_SmallModel_BecomesPresentReadyAndVerifiable()
    {
        if (!IsEnabled)
        {
            // Opt-in gate (plan §10): the stubbed-runtime integration suite covers the default path.
            return;
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(ApiBaseUrl) };

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });
        Assert.True(loginResponse.IsSuccessStatusCode, $"Login failed: {loginResponse.StatusCode}");

        using var downloadResponse = await client.PostAsJsonAsync("/api/models/download", new { modelKind = "embedding", modelId = DownloadModelId });
        Assert.Equal(HttpStatusCode.Accepted, downloadResponse.StatusCode);
        var downloadPayload = await downloadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var operationId = downloadPayload.GetProperty("operationId").GetGuid();

        // Wait for the worker to drive the operation to completion (real pull can take minutes).
        await WaitForAsync(async () =>
        {
            var status = await GetOperationStatusAsync(operationId);
            return string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromMinutes(20));

        var finalStatus = await GetOperationStatusAsync(operationId);
        Assert.Equal("completed", finalStatus);

        // The model is actually present in the dedicated Ollama instance.
        using var ollamaClient = new HttpClient { BaseAddress = new Uri(OllamaBaseUrl) };
        var tags = await ollamaClient.GetFromJsonAsync<JsonElement>("/api/tags");
        var present = tags.GetProperty("models").EnumerateArray().Any(m =>
        {
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            var model = m.TryGetProperty("model", out var mm) ? mm.GetString() : null;
            return string.Equals(name, DownloadModelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, $"{DownloadModelId}:latest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(model, DownloadModelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model, $"{DownloadModelId}:latest", StringComparison.OrdinalIgnoreCase);
        });
        Assert.True(present, $"Expected '{DownloadModelId}' to be present in /api/tags of the dedicated Ollama container.");

        // model_runtime_state reached ready.
        var stateStatus = await GetModelRuntimeStateStatusAsync("ollama", DownloadModelId);
        Assert.Equal("ready", stateStatus);

        // Verify reports verified:true.
        using var verifyResponse = await client.PostAsJsonAsync("/api/models/verify", new { modelKind = "embedding", modelId = DownloadModelId });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verifyPayload.GetProperty("verified").GetBoolean());
    }

    private async Task StartPostgresContainerAsync()
    {
        var containerId = await RunProcessAsync("docker",
        [
            "run", "--rm", "-d", "--name", _postgresContainerName,
            "-e", $"POSTGRES_USER={Username}",
            "-e", $"POSTGRES_PASSWORD={Password}",
            "-e", $"POSTGRES_DB={DatabaseName}",
            "-p", $"{_postgresPort}:5432",
            PostgresImageName
        ]);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the model-download E2E PostgreSQL container.");
        }
    }

    private async Task StartOllamaContainerAsync()
    {
        var containerId = await RunProcessAsync("docker",
        [
            "run", "--rm", "-d", "--name", _ollamaContainerName,
            "-e", "OLLAMA_HOST=0.0.0.0:11434",
            "-v", $"{_ollamaVolumeName}:/root/.ollama",
            "-p", $"{_ollamaPort}:11434",
            OllamaImageName
        ]);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the model-download E2E Ollama container.");
        }
    }

    private async Task WaitForPostgresAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("Timed out waiting for the model-download E2E PostgreSQL container.");
    }

    private Task StartApiAsync() => StartServiceAsync(
        projectRelativePath: Path.Combine("src", "StreamingDigest.Api", "StreamingDigest.Api.csproj"),
        urls: ApiBaseUrl,
        readinessUrl: $"{ApiBaseUrl}/api/setup/status",
        isApi: true,
        processSetter: p => _apiProcess = p);

    private Task StartWorkerAsync() => StartServiceAsync(
        projectRelativePath: Path.Combine("src", "StreamingDigest.Worker", "StreamingDigest.Worker.csproj"),
        urls: null,
        readinessUrl: null,
        isApi: false,
        processSetter: p => _workerProcess = p);

    private async Task StartServiceAsync(string projectRelativePath, string? urls, string? readinessUrl, bool isApi, Action<Process> processSetter)
    {
        var projectPath = Path.Combine(_repoRoot, projectRelativePath);
        var output = new List<string>();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        if (urls is not null)
        {
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(urls);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__streamingdigest"] = _connectionString!;
        startInfo.Environment["ConnectionStrings__postgres"] = _connectionString!;
        // Point both API and worker at the dedicated Ollama container (unique per-run volume).
        startInfo.Environment["embedding__ollamaEndpoint"] = OllamaBaseUrl;
        startInfo.Environment["embedding__endpoint"] = OllamaBaseUrl;
        startInfo.Environment["STREAMINGDIGEST_EMBEDDING_OLLAMA_ENDPOINT"] = OllamaBaseUrl;
        startInfo.Environment["OLLAMA_BASE_URL"] = OllamaBaseUrl;

        if (isApi)
        {
            startInfo.Environment["BOOTSTRAP_ADMIN_USERNAME"] = AdminUsername;
            startInfo.Environment["BOOTSTRAP_ADMIN_PASSWORD"] = AdminPassword;
        }
        else
        {
            startInfo.Environment["BOOTSTRAP_ADMIN_USERNAME"] = string.Empty;
            startInfo.Environment["BOOTSTRAP_ADMIN_PASSWORD"] = string.Empty;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {projectRelativePath} for the model-download E2E test.");
        processSetter(process);
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.Add(args.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.Add(args.Data);
                }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (readinessUrl is null)
        {
            // Worker has no HTTP surface; give it a moment to boot and fail fast if it crashed.
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (process.HasExited)
            {
                string logs;
                lock (output)
                {
                    logs = string.Join(Environment.NewLine, output);
                }

                throw new InvalidOperationException($"The worker process exited prematurely during the model-download E2E test.{Environment.NewLine}{logs}");
            }

            return;
        }

        try
        {
            await WaitForHttpAsync(readinessUrl, TimeSpan.FromSeconds(90));
        }
        catch (Exception ex)
        {
            string logs;
            lock (output)
            {
                logs = string.Join(Environment.NewLine, output);
            }

            throw new InvalidOperationException($"{projectRelativePath} failed to start for the model-download E2E test.{Environment.NewLine}{logs}", ex);
        }
    }

    private async Task<string?> GetOperationStatusAsync(Guid operationId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM public.operations WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", operationId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<string?> GetModelRuntimeStateStatusAsync(string provider, string modelId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status FROM public.model_runtime_state WHERE provider = @provider AND model_id = @modelId",
            connection);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("modelId", modelId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition was not met within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WaitForHttpAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if ((int)response.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Retry until timeout.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for '{url}'.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private async Task TryDockerAsync(params string[] args)
    {
        try
        {
            await RunProcessAsync("docker", args);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            process.Dispose();
        }
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
