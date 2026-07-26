using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresMigrationRunner
{
    private readonly string _connectionString;
    private const string DbSchemaVersionSettingKey = "system.dbSchemaVersion";

    public PostgresMigrationRunner(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var assembly = typeof(PostgresMigrationRunner).Assembly;
        var resourceNames = assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(".Persistence.Postgres.Scripts.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("No embedded PostgreSQL migration scripts were found.");
        }

        var currentSchemaVersion = ResolveCurrentSchemaVersion(resourceNames);
        foreach (var resourceName in resourceNames)
        {
            try
            {
                var script = LoadEmbeddedScript(assembly, resourceName);
                await using var command = new NpgsqlCommand(script, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed while applying PostgreSQL migration script '{resourceName}'.", ex);
            }
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO public.app_settings (key, value_json, updated_at)
            VALUES (@key, CAST(@valueJson AS jsonb), CURRENT_TIMESTAMP)
            ON CONFLICT (key) DO UPDATE SET
                value_json = EXCLUDED.value_json,
                updated_at = EXCLUDED.updated_at;
            """,
            new { key = DbSchemaVersionSettingKey, valueJson = $"\"{currentSchemaVersion}\"" });
    }

    public static string CurrentSchemaVersion => ResolveCurrentSchemaVersion(GetMigrationResourceNames());

    private static string[] GetMigrationResourceNames()
    {
        var assembly = typeof(PostgresMigrationRunner).Assembly;
        return assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(".Persistence.Postgres.Scripts.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveCurrentSchemaVersion(IEnumerable<string> resourceNames)
    {
        var latest = resourceNames.LastOrDefault();
        if (string.IsNullOrWhiteSpace(latest))
        {
            throw new InvalidOperationException("Unable to resolve the current PostgreSQL schema version because no migration scripts were found.");
        }

        var fileName = latest.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? latest;
        var marker = ".Persistence.Postgres.Scripts.";
        var markerIndex = latest.IndexOf(marker, StringComparison.Ordinal);
        var migrationToken = markerIndex >= 0
            ? latest[(markerIndex + marker.Length)..]
            : fileName;
        migrationToken = migrationToken.Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase);
        var match = Regex.Match(migrationToken, @"^(?<version>\d+)");
        return match.Success ? match.Groups["version"].Value : migrationToken;
    }

    private static string LoadEmbeddedScript(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded migration script '{resourceName}' could not be loaded.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
