using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace StreamingDigest.Application.Configuration;

public sealed class ApplicationConfiguration
{
    public string ConfigSchemaVersion { get; init; } = "1.0.0";

    public AppSettings App { get; init; } = new();

    public RuntimeSettings Runtime { get; init; } = new();

    public ConnectionStringsSettings ConnectionStrings { get; init; } = new();

    public LoggingSettings Logging { get; init; } = new();
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

public sealed class ConnectionStringsSettings
{
    public string StreamingDigest { get; init; } = "Host=localhost;Port=5432;Database=streamingdigest;Username=postgres;******";
}

public sealed class LoggingSettings
{
    public string Level { get; init; } = "Information";
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
        var schemaText = File.ReadAllText(schemaFilePath);
        var configNode = JsonNode.Parse(configText);

        if (configNode is null)
        {
            throw new InvalidOperationException($"Application configuration file '{configFilePath}' is empty or not valid JSON.");
        }

        var schema = JsonSchema.FromText(schemaText);
        var evaluationResult = schema.Evaluate(configNode, new EvaluationOptions());

        if (!evaluationResult.IsValid)
        {
            var messages = (evaluationResult.Errors ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Select(error => $"{error.Key}: {error.Value}")
                .Distinct(StringComparer.Ordinal)
                .Take(10);

            throw new InvalidOperationException(
                $"Application configuration validation failed for '{configFilePath}'.{Environment.NewLine}{string.Join(Environment.NewLine, messages)}");
        }

        var configuration = JsonSerializer.Deserialize<ApplicationConfiguration>(configText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return configuration ?? throw new InvalidOperationException($"Application configuration file '{configFilePath}' could not be deserialized.");
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
