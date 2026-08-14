using System.Text.Json;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public class PwaAssetTests
{
    [Fact]
    public void Manifest_ContainsRequiredInstallableMetadata()
    {
        var webRoot = GetWebRootDirectory();
        var manifestPath = Path.Combine(webRoot, "manifest.json");

        Assert.True(File.Exists(manifestPath));

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal("Streaming Digest", root.GetProperty("name").GetString());
        Assert.Equal("Stream Digest", root.GetProperty("short_name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("#0f172a", root.GetProperty("theme_color").GetString());
        Assert.Equal("#0f172a", root.GetProperty("background_color").GetString());

        var icons = root.GetProperty("icons");
        Assert.Contains(icons.EnumerateArray(), icon => icon.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons.EnumerateArray(), icon => icon.GetProperty("sizes").GetString() == "512x512");
        Assert.Contains(icons.EnumerateArray(), icon => icon.GetProperty("purpose").GetString() == "maskable");
    }

    [Fact]
    public void ServiceWorker_RegistersInstallAndActivateLifecycles()
    {
        var webRoot = GetWebRootDirectory();
        var serviceWorkerPath = Path.Combine(webRoot, "service-worker.js");

        Assert.True(File.Exists(serviceWorkerPath));

        var content = File.ReadAllText(serviceWorkerPath);

        Assert.Contains("self.addEventListener('install'", content);
        Assert.Contains("self.skipWaiting()", content);
        Assert.Contains("self.addEventListener('activate'", content);
        Assert.Contains("self.clients.claim()", content);
    }

    [Fact]
    public void IndexHtml_WiresManifestRegistrationAndMobileViewport()
    {
        var webRoot = GetWebRootDirectory();
        var indexPath = Path.Combine(webRoot, "index.html");

        Assert.True(File.Exists(indexPath));

        var content = File.ReadAllText(indexPath);

        Assert.Contains("<meta name=\"viewport\"", content);
        Assert.Contains("width=device-width", content);
        Assert.Contains("<link rel=\"manifest\"", content);
        Assert.Contains("_framework/blazor.webassembly.js", content);
        Assert.Contains("navigator.serviceWorker.register", content);
    }

    [Fact]
    public void AppSettings_DefaultsClientBootstrapToSameOrigin()
    {
        var webRoot = GetWebRootDirectory();
        var appSettingsPath = Path.Combine(webRoot, "appsettings.json");

        Assert.True(File.Exists(appSettingsPath));

        using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var root = document.RootElement;

        Assert.Equal("/", root.GetProperty("Api").GetProperty("BaseUrl").GetString());
    }

    [Fact]
    public void DevelopmentAppSettings_ExposeStandaloneApiBaseUrlOverride()
    {
        var webRoot = GetWebRootDirectory();
        var appSettingsPath = Path.Combine(webRoot, "appsettings.Development.json");

        Assert.True(File.Exists(appSettingsPath));

        using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var root = document.RootElement;

        Assert.Equal("http://localhost:5149", root.GetProperty("Api").GetProperty("BaseUrl").GetString());
    }

    [Fact]
    public void BuildOutput_ContainsBlazorBootAssets()
    {
        var frameworkRoot = GetBlazorFrameworkOutputDirectory();
        var frameworkFiles = Directory.GetFiles(frameworkRoot);

        Assert.Contains(frameworkFiles, path => Path.GetFileName(path).StartsWith("dotnet.native.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(frameworkFiles, path => Path.GetFileName(path).StartsWith("dotnet.runtime.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(frameworkFiles, path => Path.GetFileName(path).StartsWith("StreamingDigest.Web.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetWebRootDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "StreamingDigest.Web", "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the StreamingDigest.Web wwwroot directory.");
    }

    private static string GetBlazorFrameworkOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "StreamingDigest.Web", "bin", "Debug", "net10.0", "wwwroot", "_framework");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the StreamingDigest.Web build output framework directory.");
    }
}
