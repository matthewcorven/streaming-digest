using System.Reflection;
using Npgsql;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class PostgresMigrationRunner
{
    private readonly string _connectionString;

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

        foreach (var resourceName in resourceNames)
        {
            var script = LoadEmbeddedScript(assembly, resourceName);
            await using var command = new NpgsqlCommand(script, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
