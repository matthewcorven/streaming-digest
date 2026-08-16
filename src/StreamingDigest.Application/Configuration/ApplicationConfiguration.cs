using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace StreamingDigest.Application.Configuration;

public sealed class ApplicationConfiguration
{
    public string AppVersion { get; init; } = "0.0.0";

    public string ConfigSchemaVersion { get; init; } = "1.0.0";

    public string DeploymentSchemaVersion { get; init; } = "1.0.0";

    public AppSettings App { get; init; } = new();

    public RuntimeSettings Runtime { get; init; } = new();

    public ScrapingSettings Scraping { get; init; } = new();

    public BackupSettings Backup { get; init; } = new();

    public ConnectionStringsSettings ConnectionStrings { get; init; } = new();

    public LoggingSettings Logging { get; init; } = new();

    public IngestionSettings Ingestion { get; init; } = new();
}

public sealed class AppSettings
{
    public string Name { get; init; } = "streaming-digest";

    public string Environment { get; init; } = "Development";

    public string MutableSettingsStore { get; init; } = "file";
}

public sealed class RuntimeSettings
{
    public bool EnableHttpRedirect { get; init; } = true;

    public string DefaultTheme { get; init; } = "system";

    public int PaginationPageSize { get; init; } = 25;
}

public sealed class ScrapingSettings
{
    public bool RespectRobotsTxtByDefault { get; init; } = true;

    public int RateLimitDelayMs { get; init; } = 1000;

    public List<ScrapingDomainOverride> DomainOverrides { get; init; } = new();
}

public sealed class ScrapingDomainOverride
{
    public string Domain { get; init; } = string.Empty;

    public bool RespectRobotsTxt { get; init; } = true;
}

public static class ScrapingPolicyResolver
{
    public static bool ShouldRespectRobotsTxt(string? url, ScrapingSettings? settings)
    {
        if (settings is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return settings.RespectRobotsTxtByDefault;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
        {
            return settings.RespectRobotsTxtByDefault;
        }

        foreach (var domainOverride in settings.DomainOverrides.Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Domain)))
        {
            var domain = domainOverride.Domain.Trim();
            if (string.Equals(parsedUrl.Host, domain, StringComparison.OrdinalIgnoreCase) ||
                parsedUrl.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
            {
                return domainOverride.RespectRobotsTxt;
            }
        }

        return settings.RespectRobotsTxtByDefault;
    }
}

public sealed class ConnectionStringsSettings
{
    public string StreamingDigest { get; init; } = "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;******";
}

public sealed class LoggingSettings
{
    public string Level { get; init; } = "Information";
}

public sealed class BackupSettings
{
    public string DestinationPath { get; init; } = "/var/backups/streaming-digest";

    public string MediaPath { get; init; } = "/var/lib/streaming-digest/media";

    public string MatrixPath { get; init; } = "/var/lib/streaming-digest/matrix";

    public bool IncludeAppSettings { get; init; } = true;

    public bool IncludeSecrets { get; init; } = true;

    /// <summary>
    /// Maximum age in hours for a backup to be considered compliant with retention policy.
    /// Default: 72 hours (3 days).
    /// </summary>
    public int? MaxAgeHours { get; init; } = 72;

    /// <summary>
    /// Minimum number of backups required to be considered compliant with retention policy.
    /// Default: 2.
    /// </summary>
    public int? MinimumBackupCount { get; init; } = 2;
}

public sealed class IngestionSettings
{
    /// <summary>
    /// Preferred metadata adapter: "ytdlp" or "youtube_api".
    /// If set to "youtube_api" and API key is missing, falls back to "ytdlp".
    /// Defaults to "youtube_api" if available, otherwise "ytdlp".
    /// </summary>
    public string PreferredMetadataSource { get; init; } = "youtube_api";

    /// <summary>
    /// YouTube Data API v3 key. If empty, YouTube API adapter is unavailable.
    /// </summary>
    public string? YouTubeApiKey { get; init; }

    /// <summary>
    /// Minimum video duration in seconds to be considered long-form.
    /// Videos shorter than this are filtered out during ingestion.
    /// Default: 61 seconds (to exclude shorts and clips).
    /// </summary>
    public int MinDurationSeconds { get; init; } = 61;

    /// <summary>
    /// Default max age in days for video discovery.
    /// Videos older than (now - defaultMaxAgeDays) are excluded.
    /// Can be overridden per channel via channels.default_max_age_days.
    /// Default: 30 days.
    /// </summary>
    public int DefaultMaxAgeDays { get; init; } = 30;

    /// <summary>
    /// Scheduler settings for the recurring ingestion job (ADR-0011, plan §4 D2).
    /// </summary>
    public SchedulerSettings Scheduler { get; init; } = new();
}

/// <summary>
/// Controls the Hangfire recurring ingestion schedule (ADR-0011, plan §4 D2 / §10.3).
/// The default schedule fires daily at 06:00 local server time.
/// </summary>
public sealed class SchedulerSettings
{
    /// <summary>
    /// When <c>false</c>, the recurring job is removed from Hangfire at startup and no
    /// scheduled runs fire. On-demand enqueues are unaffected.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Hour of day (0–23) for the daily scheduled run, in server local time.
    /// Default: 6 (6 AM).
    /// </summary>
    public int ScheduleHour { get; init; } = 6;

    /// <summary>
    /// Minute within the hour (0–59) for the daily scheduled run.
    /// Default: 0.
    /// </summary>
    public int ScheduleMinute { get; init; } = 0;
}


public static class ApplicationConfigurationLoader
{
    private const string ConfigFileName = "appsettings.json";
    private const string ConfigSchemaFileName = "appsettings.schema.json";

    public static ApplicationConfiguration Load(string configFilePath, string schemaFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            throw new ArgumentException("Configuration file path is required.", nameof(configFilePath));
        }

        if (string.IsNullOrWhiteSpace(schemaFilePath))
        {
            throw new ArgumentException("Configuration schema path is required.", nameof(schemaFilePath));
        }

        if (!File.Exists(configFilePath))
        {
            throw new InvalidOperationException($"Application configuration file '{configFilePath}' does not exist.");
        }

        if (!File.Exists(schemaFilePath))
        {
            throw new InvalidOperationException($"Application configuration schema file '{schemaFilePath}' does not exist.");
        }

        var configText = File.ReadAllText(configFilePath);
        using var configDocument = JsonDocument.Parse(configText);

        ValidateConfigurationDocument(configDocument.RootElement, configFilePath);

        var configuration = JsonSerializer.Deserialize<ApplicationConfiguration>(configText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return configuration ?? throw new InvalidOperationException($"Application configuration file '{configFilePath}' could not be deserialized.");
    }

    private static void ValidateConfigurationDocument(JsonElement root, string configFilePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw CreateValidationException(configFilePath, "Root JSON value must be an object.");
        }

        var errors = new List<string>();

        ValidateRequiredString(root, "appVersion", errors, v => IsVersion(v, configFilePath));
        ValidateRequiredString(root, "configSchemaVersion", errors, v => IsVersion(v, configFilePath));
        ValidateRequiredString(root, "deploymentSchemaVersion", errors, v => IsVersion(v, configFilePath));

        if (TryGetObject(root, "app", out var appElement))
        {
            ValidateRequiredString(appElement, "name", errors, v => !string.IsNullOrWhiteSpace(v));
            ValidateRequiredString(appElement, "environment", errors, v => IsAllowed(v, ["Development", "Staging", "Production"]));
            ValidateRequiredString(appElement, "mutableSettingsStore", errors, v => IsAllowed(v, ["file", "database"]));
        }
        else
        {
            errors.Add("app: required object is missing.");
        }

        if (TryGetObject(root, "runtime", out var runtimeElement))
        {
            ValidateBoolean(runtimeElement, "enableHttpRedirect", errors);
            ValidateRequiredString(runtimeElement, "defaultTheme", errors, v => IsAllowed(v, ["light", "dark", "system"]));
            ValidateInteger(runtimeElement, "paginationPageSize", errors, min: 10, max: 200);
        }
        else
        {
            errors.Add("runtime: required object is missing.");
        }

        if (TryGetObject(root, "connectionStrings", out var connectionStringsElement))
        {
            ValidateRequiredString(connectionStringsElement, "streamingdigest", errors, v => !string.IsNullOrWhiteSpace(v));
        }
        else
        {
            errors.Add("connectionStrings: required object is missing.");
        }

        if (TryGetObject(root, "logging", out var loggingElement))
        {
            ValidateRequiredString(loggingElement, "level", errors, v => IsAllowed(v, ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]));
        }
        else
        {
            errors.Add("logging: required object is missing.");
        }

        if (errors.Count > 0)
        {
            throw CreateValidationException(configFilePath, string.Join(Environment.NewLine, errors.Take(10).Distinct(StringComparer.Ordinal)));
        }
    }

    private static bool TryGetObject(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static void ValidateRequiredString(JsonElement parent, string propertyName, List<string> errors, Func<string, bool> predicate)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{propertyName}: expected string value.");
            return;
        }

        var value = property.GetString();
        if (!string.IsNullOrWhiteSpace(value) && predicate(value))
        {
            return;
        }

        errors.Add($"{propertyName}: invalid value '{value}'.");
    }

    private static void ValidateBoolean(JsonElement parent, string propertyName, List<string> errors)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
        {
            errors.Add($"{propertyName}: expected boolean value.");
        }
    }

    private static void ValidateInteger(JsonElement parent, string propertyName, List<string> errors, int min, int max)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            errors.Add($"{propertyName}: expected integer value.");
            return;
        }

        if (value < min || value > max)
        {
            errors.Add($"{propertyName}: value {value} is outside the allowed range [{min}, {max}].");
        }
    }

    private static bool IsVersion(string value, string configFilePath)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsAllowed(string value, string[] allowedValues)
    {
        return allowedValues.Contains(value, StringComparer.Ordinal);
    }

    private static InvalidOperationException CreateValidationException(string configFilePath, string message)
    {
        return new InvalidOperationException($"Application configuration validation failed for '{configFilePath}'.{Environment.NewLine}{message}");
    }

    public static ApplicationConfiguration LoadFromDirectory(string? directoryPath)
    {
        var configFilePath = ResolveExistingFilePath(directoryPath, ConfigFileName);
        var schemaFilePath = ResolveExistingFilePath(directoryPath, ConfigSchemaFileName);

        return Load(configFilePath, schemaFilePath);
    }

    private static string ResolveExistingFilePath(string? directoryPath, string fileName)
    {
        var candidateDirectories = new List<string>();

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            candidateDirectories.Add(Path.GetFullPath(directoryPath));
        }

        candidateDirectories.Add(Directory.GetCurrentDirectory());
        candidateDirectories.Add(AppContext.BaseDirectory);

        foreach (var candidateDirectory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidatePath = Path.Combine(candidateDirectory, fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new InvalidOperationException($"Could not locate '{fileName}'. Checked: {string.Join(", ", candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }
}
