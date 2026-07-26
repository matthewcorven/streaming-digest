using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StreamingDigest.Application.Configuration;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.Worker;

namespace StreamingDigest.UnitTests;

public sealed class AppSettingsSeederTests : IAsyncLifetime
{
    private const string ImageName = "postgres:16-alpine";
    private const string DatabaseName = "postgres";
    private const string Username = "postgres";
    private const string Password = "postgres";

    private readonly string _containerName = $"streaming-digest-settings-tests-{Guid.NewGuid():N}";
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
    public async Task Seed_defaults_adds_missing_settings_without_overwriting_existing_values()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        await using (var connection = new NpgsqlConnection(_connectionString!))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO public.app_settings (key, value_json, updated_at) VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)",
                connection);
            command.Parameters.AddWithValue("key", "search.textWeight");
            command.Parameters.AddWithValue("valueJson", "0.99");
            await command.ExecuteNonQueryAsync();

            await using var overwriteCommand = new NpgsqlCommand(
                "INSERT INTO public.app_settings (key, value_json, updated_at) VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)",
                connection);
            overwriteCommand.Parameters.AddWithValue("key", "observability.enabled");
            overwriteCommand.Parameters.AddWithValue("valueJson", "false");
            await overwriteCommand.ExecuteNonQueryAsync();
        }

        var seeder = new AppSettingsSeeder();
        await seeder.SeedDefaultsAsync(_connectionString!);

        await using (var connection = new NpgsqlConnection(_connectionString!))
        {
            await connection.OpenAsync();

            Assert.Equal("0.99", await GetSettingValueAsync(connection, "search.textWeight"));
            Assert.Equal("false", await GetSettingValueAsync(connection, "observability.enabled"));
            Assert.Equal("false", await GetSettingValueAsync(connection, "debug.rawHtmlCapture.enabledDefault"));

            var expectedConcurrencyDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkerConcurrencySettings.DefaultChannelConcurrencyKey] = "1",
                [WorkerConcurrencySettings.ScheduledChannelConcurrencyKey] = "1",
                [WorkerConcurrencySettings.ManualChannelConcurrencyKey] = "1",
                [WorkerConcurrencySettings.BackfillChannelConcurrencyKey] = "1",
                [WorkerConcurrencySettings.VideoPerChannelConcurrencyKey] = "1",
                [WorkerConcurrencySettings.ScreenshotConcurrencyKey] = "1",
                [WorkerConcurrencySettings.EmbeddingBatchSizeKey] = "16",
                [WorkerConcurrencySettings.EmbeddingWorkerConcurrencyKey] = "1",
                [WorkerConcurrencySettings.WebsiteScrapeGlobalConcurrencyKey] = "2",
                [WorkerConcurrencySettings.WebsiteScrapePerHostConcurrencyKey] = "1",
                [WorkerConcurrencySettings.RepositoryApiGlobalConcurrencyKey] = "2",
                [WorkerConcurrencySettings.RepositoryApiPerHostConcurrencyKey] = "1",
                [WorkerConcurrencySettings.WhisperGlobalConcurrencyKey] = "1",
                [WorkerConcurrencySettings.LlmJobGlobalConcurrencyKey] = "1"
            };

            foreach (var expectedSetting in expectedConcurrencyDefaults)
            {
                Assert.Equal(expectedSetting.Value, await GetSettingValueAsync(connection, expectedSetting.Key));
            }
        }
    }

    [Fact]
    public async Task Seed_defaults_uses_supplied_observability_defaults_when_provided()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        var seeder = new AppSettingsSeeder();
        await seeder.SeedDefaultsAsync(_connectionString!, false, 30);

        await using (var connection = new NpgsqlConnection(_connectionString!))
        {
            await connection.OpenAsync();

            Assert.Equal("false", await GetSettingValueAsync(connection, "observability.enabled"));
            Assert.Equal("30", await GetSettingValueAsync(connection, "observability.retentionDays"));
            Assert.Equal("false", await GetSettingValueAsync(connection, "observability.retentionWarning"));
        }
    }

    [Fact]
    public async Task LoadAsync_reads_seeded_concurrency_defaults_and_runtime_overrides()
    {
        var runner = new PostgresMigrationRunner(_connectionString!);
        await runner.ApplyAsync();

        var seeder = new AppSettingsSeeder();
        await seeder.SeedDefaultsAsync(_connectionString!);

        await using (var connection = new NpgsqlConnection(_connectionString!))
        {
            await connection.OpenAsync();
            await UpdateSettingValueAsync(connection, WorkerConcurrencySettings.ScreenshotConcurrencyKey, "3");
            await UpdateSettingValueAsync(connection, WorkerConcurrencySettings.WebsiteScrapeGlobalConcurrencyKey, "4");
        }

        var settings = new WorkerConcurrencySettings();
        await WorkerConcurrencySettingsLoader.LoadAsync(_connectionString!, true, NullLogger.Instance, settings);

        Assert.Equal(WorkerConcurrencySettings.DefaultChannelConcurrencyValue, settings.DefaultChannelConcurrency);
        Assert.Equal(WorkerConcurrencySettings.ScheduledChannelConcurrencyValue, settings.ScheduledChannelConcurrency);
        Assert.Equal(WorkerConcurrencySettings.ManualChannelConcurrencyValue, settings.ManualChannelConcurrency);
        Assert.Equal(WorkerConcurrencySettings.BackfillChannelConcurrencyValue, settings.BackfillChannelConcurrency);
        Assert.Equal(WorkerConcurrencySettings.VideoPerChannelConcurrencyValue, settings.VideoPerChannelConcurrency);
        Assert.Equal(3, settings.ScreenshotConcurrency);
        Assert.Equal(WorkerConcurrencySettings.EmbeddingBatchSizeValue, settings.EmbeddingBatchSize);
        Assert.Equal(WorkerConcurrencySettings.EmbeddingWorkerConcurrencyValue, settings.EmbeddingWorkerConcurrency);
        Assert.Equal(4, settings.WebsiteScrapeGlobalConcurrency);
        Assert.Equal(WorkerConcurrencySettings.WebsiteScrapePerHostConcurrencyValue, settings.WebsiteScrapePerHostConcurrency);
        Assert.Equal(WorkerConcurrencySettings.RepositoryApiGlobalConcurrencyValue, settings.RepositoryApiGlobalConcurrency);
        Assert.Equal(WorkerConcurrencySettings.RepositoryApiPerHostConcurrencyValue, settings.RepositoryApiPerHostConcurrency);
        Assert.Equal(WorkerConcurrencySettings.WhisperGlobalConcurrencyValue, settings.WhisperGlobalConcurrency);
        Assert.Equal(WorkerConcurrencySettings.LlmJobGlobalConcurrencyValue, settings.LlmJobGlobalConcurrency);
    }

    private static async Task<string> GetSettingValueAsync(NpgsqlConnection connection, string key)
    {
        await using var command = new NpgsqlCommand("SELECT value_json::text FROM public.app_settings WHERE key = @key", connection);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        return reader.GetString(0);
    }

    private static async Task UpdateSettingValueAsync(NpgsqlConnection connection, string key, string valueJson)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE public.app_settings SET value_json = CAST(@valueJson AS jsonb), updated_at = CURRENT_TIMESTAMP WHERE key = @key",
            connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("valueJson", valueJson);
        await command.ExecuteNonQueryAsync();
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
            throw new InvalidOperationException("Docker did not return a container id for the PostgreSQL test container.");
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

        throw new TimeoutException("Timed out waiting for the PostgreSQL test container to become ready.");
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
