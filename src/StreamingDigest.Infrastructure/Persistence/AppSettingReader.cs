using System.Data.Common;
using System.Text.Json;

namespace StreamingDigest.Infrastructure.Persistence;

public static class AppSettingReader
{
    public static async Task<string?> ReadJsonAsync(DbConnection connection, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json::text FROM public.app_settings WHERE key = @key LIMIT 1";

        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "@key";
        keyParameter.Value = key;
        command.Parameters.Add(keyParameter);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public static async Task<bool> ReadBoolAsync(DbConnection connection, string key, bool fallback, CancellationToken cancellationToken = default)
    {
        return TryParseBool(await ReadJsonAsync(connection, key, cancellationToken), out var value) ? value : fallback;
    }

    public static async Task<int> ReadIntAsync(DbConnection connection, string key, int fallback, CancellationToken cancellationToken = default)
    {
        return TryParseInt(await ReadJsonAsync(connection, key, cancellationToken), out var value) ? value : fallback;
    }

    public static async Task<string> ReadStringAsync(DbConnection connection, string key, string fallback, CancellationToken cancellationToken = default)
    {
        return TryParseString(await ReadJsonAsync(connection, key, cancellationToken), out var value) ? value : fallback;
    }

    public static bool TryParseBool(string? json, out bool value)
    {
        if (bool.TryParse(json, out value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            value = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = document.RootElement.GetBoolean();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        value = default;
        return false;
    }

    public static bool TryParseInt(string? json, out int value)
    {
        if (int.TryParse(json, out value))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            value = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Number && document.RootElement.TryGetInt32(out value))
            {
                return true;
            }
        }
        catch (JsonException)
        {
        }

        value = default;
        return false;
    }

    public static bool TryParseString(string? json, out string value)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            value = string.Empty;
            return false;
        }

        try
        {
            var parsedValue = JsonSerializer.Deserialize<string>(json);
            if (parsedValue is not null)
            {
                value = parsedValue;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        value = string.Empty;
        return false;
    }
}
