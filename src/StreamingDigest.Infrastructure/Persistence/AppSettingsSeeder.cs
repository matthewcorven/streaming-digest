using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Application.Configuration;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class AppSettingsSeeder(ILogger? logger = null)
{
    public async Task SeedDefaultsAsync(string connectionString, bool? defaultObservabilityEnabled = null, int? defaultRetentionDays = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required to seed app settings.", nameof(connectionString));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            foreach (var setting in BuildDefaultSettings(defaultObservabilityEnabled, defaultRetentionDays))
            {
                try
                {
                    var value = JsonSerializer.Serialize(setting.Value);
                    await connection.ExecuteAsync(
                        """
                        INSERT INTO public.app_settings (key, value_json, updated_at)
                        VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)
                        ON CONFLICT (key) DO NOTHING;
                        """,
                        new { key = setting.Key, valueJson = value });
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to seed app setting {SettingKey}", setting.Key);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to seed application settings defaults");
        }
    }

    private static IReadOnlyDictionary<string, object> BuildDefaultSettings(bool? defaultObservabilityEnabled = null, int? defaultRetentionDays = null)
    {
        var freeSpaceBytes = StorageRetentionPolicy.GetMaximumReadyDriveFreeSpaceBytes();
        var maxBytes = StorageRetentionPolicy.ComputeTempMediaMaxBytes(freeSpaceBytes);
        var observabilityEnabled = defaultObservabilityEnabled ?? false;
        var retentionDays = defaultRetentionDays ?? StorageRetentionPolicy.ComputeObservabilityRetentionDays(freeSpaceBytes);

        var defaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["ingestion.defaultMaxAgeDays"] = 30,
            ["ingestion.defaultConcurrency"] = 1,
            ["ingestion.maxSegmentsPerVideo"] = 60,
            ["ingestion.defaultScheduleLocalTime"] = "06:00",
            ["ingestion.scheduleTimeZone"] = "UTC",
            ["ingestion.minDurationSeconds"] = 61,
            ["ingestion.tempMedia.maxBytes"] = maxBytes,
            ["screenshots.offsetSeconds"] = 5,
            ["search.highSignalThresholdPercent"] = 70,
            ["search.interactionBoostWindowDays"] = 90,
            ["search.textWeight"] = 0.7,
            ["search.vectorWeight"] = 0.3,
            ["notifications.matrix.enabled"] = false,
            ["notifications.matrix.homeserverUrl"] = "https://matrix-client.matrix.org",
            ["notifications.matrix.botUserId"] = "@streamingdigest:matrix.org",
            ["notifications.matrix.roomId"] = string.Empty,
            ["notifications.matrix.onManualRuns"] = true,
            ["notifications.matrix.onScheduledRuns"] = true,
            ["notifications.matrix.onBackfillRuns"] = false,
            ["notifications.matrix.dashboardBaseUrl"] = "http://localhost:8080",
            ["observability.enabled"] = observabilityEnabled,
            ["observability.retentionDays"] = retentionDays,
            ["observability.retentionWarning"] = StorageRetentionPolicy.HasObservabilityRetentionWarning(retentionDays),
            ["observability.links.grafanaUrl"] = "http://localhost:3000",
            ["observability.links.pgadminUrl"] = "http://localhost:5050",
            ["observability.links.prometheusUrl"] = "http://localhost:9090",
            ["observability.links.lokiUrl"] = "http://localhost:3100",
            ["observability.links.tempoUrl"] = "http://localhost:3200",
            ["observability.links.hangfireUrl"] = "/admin/jobs",
            ["debug.rawHtmlCapture.enabledDefault"] = false
        };

        foreach (var setting in WorkerConcurrencySettings.CreateSeedDefaults())
        {
            defaults[setting.Key] = setting.Value;
        }

        return defaults;
    }
}
