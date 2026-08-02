using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Playwright;
using Npgsql;

namespace StreamingDigest.E2E;

public sealed class FirstRunSetupE2ETests : IAsyncLifetime
{
    private const string ImageName = "pgvector/pgvector:0.8.5-pg18-trixie";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private static readonly SemaphoreSlim BrowserInstallLock = new(1, 1);
    private static bool s_browserInstalled;

    private readonly string _containerName = $"streaming-digest-first-run-e2e-{Guid.NewGuid():N}";
    private readonly int _postgresPort = GetAvailablePort();
    private readonly int _appPort = GetAvailablePort();
    private readonly int _webPort = GetAvailablePort();
    private readonly string _repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private Process? _apiProcess;
    private HttpListener? _webServer;
    private string? _webRoot;
    private string? _connectionString;
    private string ApiBaseUrl => $"http://127.0.0.1:{_appPort}";
    private string WebBaseUrl => $"http://127.0.0.1:{_webPort}";

    [Fact(Skip = "E2E test requires Docker and full infrastructure setup; runs locally only")]
    public async Task Zero_user_start_requires_setup_then_blocks_setup_after_first_sign_in()
    {
        await AssertSetupRequiredAsync();
        await EnsurePlaywrightBrowsersInstalledAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var diagnostics = new List<string>();
        page.Console += (_, message) => diagnostics.Add($"console:{message.Type}:{message.Text}");
        page.PageError += (_, exception) => diagnostics.Add($"pageerror:{exception}");

        await page.GotoAsync($"{WebBaseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        try
        {
            await page.GetByText("Create the first account").WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        }
        catch (TimeoutException ex)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new TimeoutException(
                $"Timed out waiting for the setup UI. URL: {page.Url}{Environment.NewLine}Body:{Environment.NewLine}{bodyText}{Environment.NewLine}Diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}",
                ex);
        }

        await WaitForUrlAsync(page, url => UrlPathIs(url, "/setup"), TimeSpan.FromSeconds(60));

        await page.GetByLabel("Username").FillAsync("founder");
        await page.Locator("#password").FillAsync("setup-passw0rd!");
        await page.Locator("#confirm-password").FillAsync("setup-passw0rd!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await WaitForUrlAsync(page, url => UrlPathIs(url, "/login") && HasSetupSuccessQuery(url), TimeSpan.FromSeconds(60));
        await page.GetByText("Setup complete. Sign in with the account you just created.").WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await page.GetByLabel("Username").FillAsync("founder");
        await page.Locator("#password").FillAsync("setup-passw0rd!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await WaitForUrlAsync(
            page,
            url => UrlPathIsOneOf(url, "/channels", "/dashboard", "/ingestion"),
            TimeSpan.FromSeconds(60));

        await page.GotoAsync($"{WebBaseUrl}/setup", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForUrlAsync(
            page,
            url => UrlPathIsOneOf(url, "/channels", "/dashboard", "/ingestion"),
            TimeSpan.FromSeconds(60));

        await page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync();
        await WaitForUrlAsync(page, url => UrlPathIs(url, "/login"), TimeSpan.FromSeconds(60));

        await page.GotoAsync($"{WebBaseUrl}/setup", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForUrlAsync(page, url => UrlPathIs(url, "/login"), TimeSpan.FromSeconds(60));
    }

    public async Task InitializeAsync()
    {
        _connectionString = $"Host=127.0.0.1;Port={_postgresPort};Database={DatabaseName};Username={Username};Password={Password};SslMode=Disable";

        await StartPostgresContainerAsync();
        await WaitForPostgresAsync();
        await StartApiAsync();
        _webRoot = CreateE2eWebRoot();
        _webServer = await StartStaticWebProxyAsync(_webRoot);
        await WaitForHttpAsync($"{ApiBaseUrl}/api/setup/status", TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        await StopWebServerAsync();
        await StopApiAsync();
        await StopPostgresContainerAsync();

        if (!string.IsNullOrWhiteSpace(_webRoot))
        {
            try
            {
                Directory.Delete(_webRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
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
            $"{_postgresPort}:5432",
            ImageName
        };

        var containerId = await RunProcessAsync("docker", dockerArgs, _repoRoot);
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new InvalidOperationException("Docker did not return a container id for the first-run E2E PostgreSQL container.");
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

        throw new TimeoutException("Timed out waiting for the E2E PostgreSQL container to become ready.");
    }

    private async Task StartApiAsync()
    {
        var projectPath = Path.Combine(_repoRoot, "src", "StreamingDigest.Api", "StreamingDigest.Api.csproj");
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
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(ApiBaseUrl);

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ConnectionStrings__streamingdigest"] = _connectionString;
        startInfo.Environment["ConnectionStrings__postgres"] = _connectionString;
        startInfo.Environment["BOOTSTRAP_ADMIN_USERNAME"] = string.Empty;
        startInfo.Environment["BOOTSTRAP_ADMIN_PASSWORD"] = string.Empty;

        _apiProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the StreamingDigest.Api process for E2E testing.");
        _apiProcess.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.Add(args.Data);
                }
            }
        };
        _apiProcess.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                lock (output)
                {
                    output.Add(args.Data);
                }
            }
        };
        _apiProcess.BeginOutputReadLine();
        _apiProcess.BeginErrorReadLine();

        try
        {
            await WaitForHttpAsync($"{ApiBaseUrl}/api/setup/status", TimeSpan.FromSeconds(60));
        }
        catch (Exception ex)
        {
            string logs;
            lock (output)
            {
                logs = string.Join(Environment.NewLine, output);
            }

            throw new InvalidOperationException($"StreamingDigest.Api failed to start for the E2E test.{Environment.NewLine}{logs}", ex);
        }
    }

    private async Task StopApiAsync()
    {
        if (_apiProcess is null)
        {
            return;
        }

        try
        {
            if (!_apiProcess.HasExited)
            {
                _apiProcess.Kill(entireProcessTree: true);
                await _apiProcess.WaitForExitAsync();
            }
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            _apiProcess.Dispose();
            _apiProcess = null;
        }
    }

    private async Task StopPostgresContainerAsync()
    {
        try
        {
            await RunProcessAsync("docker", new[] { "rm", "-f", _containerName }, _repoRoot);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private async Task AssertSetupRequiredAsync()
    {
        using var client = new HttpClient();
        var status = await client.GetFromJsonAsync<SetupStatusResponse>($"{ApiBaseUrl}/api/setup/status");
        if (status is null)
        {
            throw new InvalidOperationException("The setup status endpoint returned no payload for the E2E test.");
        }

        if (!status.SetupRequired || status.UserCount != 0)
        {
            throw new InvalidOperationException($"Expected first-run setup to be required before the browser flow starts, but the API reported SetupRequired={status.SetupRequired} and UserCount={status.UserCount}.");
        }
    }

    private string CreateE2eWebRoot()
    {
        var sourceWebRoot = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "wwwroot");
        var builtFrameworkRoot = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "bin", "Debug", "net10.0", "wwwroot", "_framework");
        var generatedHtmlRoot = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "obj", "Debug", "net10.0", "staticwebassets", "htmlassetplaceholders", "build");
        var stylesheetPath = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "obj", "Debug", "net10.0", "scopedcss", "bundle", "StreamingDigest.Web.styles.css");
        var hotReloadModulePath = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "obj", "Debug", "net10.0", "hotreload", "Microsoft.DotNet.HotReload.WebAssembly.Browser.lib.module.js");
        var dotnetLoaderPath = Path.Combine(_repoRoot, "src", "StreamingDigest.Web", "obj", "Debug", "net10.0", "dotnet.js");
        var fluentAssetsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "microsoft.fluentui.aspnetcore.components", "4.11.0", "staticwebassets");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"streaming-digest-e2e-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        CopyDirectory(sourceWebRoot, tempRoot);
        CopyDirectory(builtFrameworkRoot, Path.Combine(tempRoot, "_framework"));
        CopyDirectory(fluentAssetsRoot, Path.Combine(tempRoot, "_content", "Microsoft.FluentUI.AspNetCore.Components"));

        File.Copy(stylesheetPath, Path.Combine(tempRoot, "StreamingDigest.Web.styles.css"), overwrite: true);
        File.Copy(hotReloadModulePath, Path.Combine(tempRoot, "_framework", Path.GetFileName(hotReloadModulePath)), overwrite: true);

        var dotnetLoaderContents = File.ReadAllText(dotnetLoaderPath);
        var hotReloadAlias = Regex.Match(dotnetLoaderContents, @"Microsoft\.DotNet\.HotReload\.WebAssembly\.Browser\.[A-Za-z0-9]+\.lib\.module\.js").Value;
        if (!string.IsNullOrWhiteSpace(hotReloadAlias))
        {
            File.Copy(
                hotReloadModulePath,
                Path.Combine(tempRoot, "_framework", hotReloadAlias),
                overwrite: true);
        }

        var generatedIndexPath = Directory.GetFiles(generatedHtmlRoot, "*.html")
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(generatedIndexPath))
        {
            throw new FileNotFoundException("Could not find the generated Blazor HTML placeholder for the E2E webroot.");
        }

        File.Copy(generatedIndexPath, Path.Combine(tempRoot, "index.html"), overwrite: true);

        var indexPath = Path.Combine(tempRoot, "index.html");
        var indexHtml = File.ReadAllText(indexPath);
        var bootScript = Directory.GetFiles(Path.Combine(tempRoot, "_framework"), "blazor.webassembly*.js")
            .Select(Path.GetFileName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)
                && name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
        var dotnetRuntimeScript = Directory.GetFiles(Path.Combine(tempRoot, "_framework"), "dotnet.*.js")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, "dotnet.js", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("dotnet.native.", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("dotnet.runtime.", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(bootScript))
        {
            throw new FileNotFoundException("Could not find the built Blazor WebAssembly boot script for the E2E webroot.");
        }

        if (string.IsNullOrWhiteSpace(dotnetRuntimeScript))
        {
            throw new FileNotFoundException("Could not find the built dotnet runtime script for the E2E webroot.");
        }

        File.Copy(
            Path.Combine(tempRoot, "_framework", dotnetRuntimeScript),
            Path.Combine(tempRoot, "_framework", "dotnet.js"),
            overwrite: true);

        indexHtml = indexHtml.Replace("_framework/blazor.webassembly#[.{fingerprint}].js", $"_framework/{bootScript}", StringComparison.Ordinal);
        File.WriteAllText(indexPath, indexHtml, Encoding.UTF8);

        return tempRoot;
    }

    private async Task<HttpListener> StartStaticWebProxyAsync(string webRoot)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{_webPort}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleWebRequestAsync(context, webRoot));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        });

        return listener;
    }

    private async Task StopWebServerAsync()
    {
        if (_webServer is null)
        {
            return;
        }

        try
        {
            _webServer.Stop();
            _webServer.Close();
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            _webServer = null;
        }
    }

    private async Task HandleWebRequestAsync(HttpListenerContext context, string webRoot)
    {
        try
        {
            var requestPath = context.Request.Url?.AbsolutePath ?? "/";
            if (requestPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await ProxyApiRequestAsync(context);
                return;
            }

            await ServeStaticAssetAsync(context, requestPath, webRoot);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8, leaveOpen: false);
            await writer.WriteAsync(ex.Message);
            context.Response.Close();
        }
    }

    private async Task ProxyApiRequestAsync(HttpListenerContext context)
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });

        var upstreamUri = new Uri($"{ApiBaseUrl}{context.Request.RawUrl}");
        using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), upstreamUri);

        foreach (string headerName in context.Request.Headers)
        {
            if (string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerValues = context.Request.Headers.GetValues(headerName);
            if (headerValues is null)
            {
                continue;
            }

            if (!requestMessage.Headers.TryAddWithoutValidation(headerName, headerValues))
            {
                requestMessage.Content ??= new StreamContent(Stream.Null);
                requestMessage.Content.Headers.TryAddWithoutValidation(headerName, headerValues);
            }
        }

        if (context.Request.HasEntityBody)
        {
            var streamContent = new StreamContent(context.Request.InputStream);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }

            requestMessage.Content = streamContent;
        }

        using var responseMessage = await client.SendAsync(requestMessage);
        context.Response.StatusCode = (int)responseMessage.StatusCode;

        foreach (var header in responseMessage.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            context.Response.Headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            context.Response.Headers[header.Key] = string.Join(",", header.Value);
        }

        await using var responseStream = await responseMessage.Content.ReadAsStreamAsync();
        await responseStream.CopyToAsync(context.Response.OutputStream);
        context.Response.Close();
    }

    private static async Task ServeStaticAssetAsync(HttpListenerContext context, string requestPath, string webRoot)
    {
        var normalizedPath = requestPath == "/" ? "/index.html" : requestPath;
        var relativePath = normalizedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var candidatePath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
        var safeWebRoot = Path.GetFullPath(webRoot);

        if (!candidatePath.StartsWith(safeWebRoot, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            context.Response.Close();
            return;
        }

        if (File.Exists(candidatePath))
        {
            await WriteFileResponseAsync(context, candidatePath);
            return;
        }

        if (string.IsNullOrEmpty(Path.GetExtension(relativePath)))
        {
            await WriteFileResponseAsync(context, Path.Combine(webRoot, "index.html"));
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }

    private static async Task WriteFileResponseAsync(HttpListenerContext context, string filePath)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = GetContentType(filePath);

        await using var stream = File.OpenRead(filePath);
        context.Response.ContentLength64 = stream.Length;
        await stream.CopyToAsync(context.Response.OutputStream);
        context.Response.Close();
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Required directory '{sourceDirectory}' was not found.");
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, destinationDirectory, StringComparison.Ordinal));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationFile = file.Replace(sourceDirectory, destinationDirectory, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static string GetContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            ".ico" => "image/x-icon",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".wasm" => "application/wasm",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static async Task EnsurePlaywrightBrowsersInstalledAsync()
    {
        if (s_browserInstalled)
        {
            return;
        }

        await BrowserInstallLock.WaitAsync();
        try
        {
            if (s_browserInstalled)
            {
                return;
            }

            var baseDirectory = AppContext.BaseDirectory;
            var bashInstaller = Path.Combine(baseDirectory, "playwright.sh");
            var powershellInstaller = Path.Combine(baseDirectory, "playwright.ps1");

            if (File.Exists(bashInstaller))
            {
                await RunProcessAsync("bash", new[] { bashInstaller, "install", "chromium" }, baseDirectory);
            }
            else if (File.Exists(powershellInstaller))
            {
                await RunProcessAsync("pwsh", new[] { "-File", powershellInstaller, "install", "chromium" }, baseDirectory);
            }
            else
            {
                throw new FileNotFoundException("Could not find the Playwright installer script in the test output directory.");
            }

            s_browserInstalled = true;
        }
        finally
        {
            BrowserInstallLock.Release();
        }
    }

    private static async Task WaitForHttpAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"Timed out waiting for '{url}'. Last error: {lastError?.Message}");
    }

    private static async Task WaitForUrlAsync(IPage page, Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (predicate(page.Url))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException($"Timed out waiting for browser URL. Last URL was '{page.Url}'.");
    }

    private static bool UrlPathIs(string url, string expectedPath)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && string.Equals(uri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool UrlPathIsOneOf(string url, params string[] expectedPaths)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && expectedPaths.Any(path => string.Equals(uri.AbsolutePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasSetupSuccessQuery(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Query.Contains("setup=success", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
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

    private sealed class SetupStatusResponse
    {
        public bool SetupRequired { get; set; }

        public long UserCount { get; set; }
    }
}