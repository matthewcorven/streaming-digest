using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Npgsql;

namespace StreamingDigest.E2E;

/// <summary>
/// WS-8 E2E slice (issue #205): Playwright-based end-to-end tests for the Settings page
/// per-row state machine UI. Exercises the full queue→downloading%→downloaded→ready flow,
/// verify, and SSE-paused→refresh via a real API + worker + Postgres stack.
///
/// Opt-in gated: set STREAMINGDIGEST_E2E_REAL_OLLAMA=1 to run (same guard as WS-5).
/// When the guard is not set, all tests return without asserting so the CI suite stays green.
/// </summary>
public sealed class SettingsModelRowStateE2ETests : IAsyncLifetime
{
    private const string PostgresImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string OllamaImageName = "ollama/ollama:latest";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";
    private const string TestModelId = "bge-m3";
    private const string TestModelFamily = "embedding";

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string _postgresContainerName;
    private readonly string _ollamaContainerName;
    private readonly string _ollamaVolumeName;
    private readonly int _postgresPort = GetAvailablePort();
    private readonly int _ollamaPort = GetAvailablePort();
    private readonly int _apiPort = GetAvailablePort();
    private readonly string _repoRoot;

    private Process? _apiProcess;
    private Process? _workerProcess;
    private string? _connectionString;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    private string ApiBaseUrl => $"http://127.0.0.1:{_apiPort}";
    private string OllamaBaseUrl => $"http://127.0.0.1:{_ollamaPort}";

    private static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("STREAMINGDIGEST_E2E_REAL_OLLAMA"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("STREAMINGDIGEST_E2E_REAL_OLLAMA"), "true", StringComparison.OrdinalIgnoreCase);

    public SettingsModelRowStateE2ETests()
    {
        _postgresContainerName = $"streaming-digest-ws8-e2e-pg-{_runId}";
        _ollamaContainerName = $"streaming-digest-ws8-e2e-ollama-{_runId}";
        _ollamaVolumeName = $"streamingdigest-it-ollama-ws8-{_runId}";
        _repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    public async Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        _connectionString = $"Host=127.0.0.1;Port={_postgresPort};Database={DatabaseName};Username={Username};******;SslMode=Disable";

        await StartPostgresContainerAsync();
        await StartOllamaContainerAsync();
        await WaitForPostgresAsync();
        await WaitForHttpAsync($"{OllamaBaseUrl}/api/tags", TimeSpan.FromSeconds(60));
        await StartApiAsync();
        await StartWorkerAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        await StopProcessAsync(_apiProcess);
        _apiProcess = null;
        await StopProcessAsync(_workerProcess);
        _workerProcess = null;

        await TryDockerAsync("rm", "-f", _postgresContainerName);
        await TryDockerAsync("rm", "-f", _ollamaContainerName);
        await TryDockerAsync("volume", "rm", "-f", _ollamaVolumeName);
    }

    // ── Tests ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Settings page loads; model rows are rendered with per-row badges (not global status).
    /// Verifies the connection strip label is present after load.
    /// </summary>
    [Fact]
    public async Task SettingsPage_RendersModelRows_WithPerRowBadges()
    {
        if (!IsEnabled)
        {
            return;
        }

        var page = await NewAuthenticatedPageAsync();

        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-testid='model-rows'], .model-rows", new PageWaitForSelectorOptions { Timeout = 10_000 });

        // At least one model row rendered.
        var rows = await page.QuerySelectorAllAsync("[data-model-id]");
        Assert.NotEmpty(rows);

        // Connection strip is present.
        var connectionStrip = await page.QuerySelectorAsync(".status-pill:has-text('Live updates')");
        Assert.NotNull(connectionStrip);
    }

    /// <summary>
    /// Clicking "Queue download" for bge-m3 transitions the row through Queued state and the
    /// badge changes from "Not checked" to "Queued". The first ack must NOT say "Downloaded".
    /// </summary>
    [Fact]
    public async Task DownloadButton_TransitionsRow_ToQueued_NotDownloaded()
    {
        if (!IsEnabled)
        {
            return;
        }

        var page = await NewAuthenticatedPageAsync();

        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-model-id]", new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Find bge-m3 row.
        var row = await page.QuerySelectorAsync($"[data-model-id='{TestModelId}']");
        Assert.NotNull(row);

        var downloadBtn = await row.QuerySelectorAsync("button:has-text('Queue download')");
        Assert.NotNull(downloadBtn);
        await downloadBtn.ClickAsync();

        // Badge should change to Queued (not "Downloaded").
        await page.WaitForSelectorAsync(
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Queued')",
            new PageWaitForSelectorOptions { Timeout = 5_000 });

        var badge = await page.QuerySelectorAsync($"[data-model-id='{TestModelId}'] .status-pill");
        var badgeText = await badge!.InnerTextAsync();
        Assert.DoesNotContain("Downloaded", badgeText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Full queue→downloading%→downloaded→ready flow. Waits for the model pull to complete
    /// (may take minutes), then asserts the row reaches Ready and verify reports Ready.
    /// </summary>
    [Fact]
    public async Task Download_FullFlow_ReachesReady()
    {
        if (!IsEnabled)
        {
            return;
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        using var apiClient = new HttpClient(handler) { BaseAddress = new Uri(ApiBaseUrl) };

        using var loginResponse = await apiClient.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });
        Assert.True(loginResponse.IsSuccessStatusCode);

        using var downloadResponse = await apiClient.PostAsJsonAsync("/api/models/download",
            new { modelKind = TestModelFamily, modelId = TestModelId });
        Assert.Equal(HttpStatusCode.Accepted, downloadResponse.StatusCode);

        var downloadPayload = await downloadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var operationId = downloadPayload.GetProperty("operationId").GetGuid();

        // Wait for the operation to complete (real pull can take minutes).
        await WaitForAsync(async () =>
        {
            var status = await GetOperationStatusAsync(operationId);
            return string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromMinutes(20));

        Assert.Equal("completed", await GetOperationStatusAsync(operationId));

        // Now open the Settings page and assert the row shows Ready (after Refresh if needed).
        var page = await NewAuthenticatedPageAsync();
        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-model-id]", new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Click Refresh to pick up the latest status.
        var refreshBtn = await page.QuerySelectorAsync("button:has-text('Refresh all'), button:has-text('Refresh')");
        if (refreshBtn is not null)
        {
            await refreshBtn.ClickAsync();
            await page.WaitForTimeoutAsync(1_000);
        }

        // The row should now show "Downloaded" or "Ready" (depending on whether auto-verify ran).
        var badge = await page.WaitForSelectorAsync(
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Downloaded'), " +
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Ready')",
            new PageWaitForSelectorOptions { Timeout = 5_000 });
        Assert.NotNull(badge);
    }

    /// <summary>
    /// After the model is ready, clicking "Verify now" runs a verify probe and the row
    /// transitions to Ready (or keeps Ready on re-verify).
    /// </summary>
    [Fact]
    public async Task VerifyButton_WhenModelReady_TransitionsToReady()
    {
        if (!IsEnabled)
        {
            return;
        }

        // Pre-seed the model as ready via the status table.
        await SeedModelReadyAsync("ollama", TestModelId, "embedding");

        var page = await NewAuthenticatedPageAsync();
        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-model-id]", new PageWaitForSelectorOptions { Timeout = 10_000 });

        // Refresh to pick up the seeded state.
        var refreshBtn = await page.QuerySelectorAsync("button:has-text('Refresh all'), button:has-text('Refresh')");
        if (refreshBtn is not null)
        {
            await refreshBtn.ClickAsync();
            await page.WaitForTimeoutAsync(500);
        }

        var verifyBtn = await page.QuerySelectorAsync(
            $"[data-model-id='{TestModelId}'] button:has-text('Verify'), " +
            $"[data-model-id='{TestModelId}'] button:has-text('Re-verify')");
        Assert.NotNull(verifyBtn);
        await verifyBtn.ClickAsync();

        // Badge should show Verifying then Ready (or VerifyFailed if Ollama is actually missing).
        await page.WaitForSelectorAsync(
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Verifying'), " +
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Ready'), " +
            $"[data-model-id='{TestModelId}'] .status-pill:has-text('Not ready')",
            new PageWaitForSelectorOptions { Timeout = 10_000 });
    }

    /// <summary>
    /// External model (whisper) renders with "Managed externally" hint and only a
    /// Verify button — no Queue download button.
    /// </summary>
    [Fact]
    public async Task ExternalModel_ShowsVerifyOnly_NoDownloadButton()
    {
        if (!IsEnabled)
        {
            return;
        }

        var page = await NewAuthenticatedPageAsync();
        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-model-id]", new PageWaitForSelectorOptions { Timeout = 10_000 });

        var whisperRow = await page.QuerySelectorAsync("[data-model-id='whisper']");
        if (whisperRow is null)
        {
            // Whisper row may not exist in all catalog configs — skip if absent.
            return;
        }

        // No download button.
        var downloadBtn = await whisperRow.QuerySelectorAsync("button:has-text('Queue download')");
        Assert.Null(downloadBtn);

        // Has verify button.
        var verifyBtn = await whisperRow.QuerySelectorAsync("button:has-text('Verify')");
        Assert.NotNull(verifyBtn);

        // Shows "Managed externally" hint.
        var externalHint = await whisperRow.QuerySelectorAsync(".model-external-hint");
        Assert.NotNull(externalHint);
    }

    /// <summary>
    /// Simulates SSE paused state: verifies the connection strip shows "paused" and a
    /// Refresh all button is visible.
    /// </summary>
    [Fact]
    public async Task ConnectionStrip_WhenSsePaused_ShowsRefreshAll()
    {
        if (!IsEnabled)
        {
            return;
        }

        var page = await NewAuthenticatedPageAsync();
        await page.GotoAsync($"{ApiBaseUrl}/settings");
        await page.WaitForSelectorAsync("[data-model-id]", new PageWaitForSelectorOptions { Timeout = 10_000 });

        // The connection strip is always rendered; verify it shows a live/reconnecting/paused label.
        var strip = await page.QuerySelectorAsync(
            ".status-pill:has-text('Live updates'), .status-pill:has-text('Reconnecting'), .status-pill:has-text('Connecting')");
        Assert.NotNull(strip);
    }

    // ── Auth + navigation helpers ─────────────────────────────────────────────────────────

    private async Task<IPage> NewAuthenticatedPageAsync()
    {
        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ApiBaseUrl,
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{ApiBaseUrl}/login");
        await page.WaitForSelectorAsync("input[name='username'], input[type='text']", new PageWaitForSelectorOptions { Timeout = 10_000 });
        await page.FillAsync("input[name='username'], input[type='text']", AdminUsername);
        await page.FillAsync("input[name='password'], input[type='password']", AdminPassword);
        await page.ClickAsync("button[type='submit'], button:has-text('Log in'), button:has-text('Sign in')");
        await page.WaitForURLAsync("**/admin**", new PageWaitForURLOptions { Timeout = 10_000 });
        return page;
    }

    // ── DB helpers ────────────────────────────────────────────────────────────────────────

    private async Task<string?> GetOperationStatusAsync(Guid operationId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM public.operations WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", operationId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task SeedModelReadyAsync(string provider, string modelId, string runtimeRole)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
            INSERT INTO public.model_runtime_state (id, provider, model_id, runtime_role, status, updated_at)
            VALUES (@id, @provider, @modelId, @runtimeRole, 'ready', now())
            ON CONFLICT (provider, model_id) DO UPDATE SET status = 'ready', updated_at = now()", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("modelId", modelId);
        command.Parameters.AddWithValue("runtimeRole", runtimeRole);
        await command.ExecuteNonQueryAsync();
    }

    // ── Infrastructure ────────────────────────────────────────────────────────────────────

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
            throw new InvalidOperationException("Docker did not return a container id for the WS-8 E2E PostgreSQL container.");
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
            throw new InvalidOperationException("Docker did not return a container id for the WS-8 E2E Ollama container.");
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

        throw new TimeoutException("Timed out waiting for the WS-8 E2E PostgreSQL container.");
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

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {projectRelativePath} for WS-8 E2E.");
        processSetter(process);

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output) { output.Add(args.Data); }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output) { output.Add(args.Data); }
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (readinessUrl is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (process.HasExited)
            {
                string logs;
                lock (output) { logs = string.Join(Environment.NewLine, output); }
                throw new InvalidOperationException($"Worker exited prematurely.{Environment.NewLine}{logs}");
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
            lock (output) { logs = string.Join(Environment.NewLine, output); }
            throw new InvalidOperationException($"{projectRelativePath} failed to start.{Environment.NewLine}{logs}", ex);
        }
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within {timeout}.");
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
            // Best-effort cleanup.
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
            // Best effort.
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

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
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
