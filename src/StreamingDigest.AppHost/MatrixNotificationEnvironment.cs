using Microsoft.Extensions.Configuration;

namespace StreamingDigest.AppHost;

public static class MatrixNotificationEnvironment
{
    private static readonly (string ConfigurationKey, string EnvironmentKey)[] Mappings =
    [
        ("notifications:matrix:enabled", "notifications__matrix__enabled"),
        ("notifications:matrix:homeserverUrl", "notifications__matrix__homeserverUrl"),
        ("notifications:matrix:accessToken", "notifications__matrix__accessToken"),
        ("notifications:matrix:roomId", "notifications__matrix__roomId"),
        ("notifications:matrix:botUserId", "notifications__matrix__botUserId"),
        ("notifications:matrix:onManualRuns", "notifications__matrix__onManualRuns"),
        ("notifications:matrix:onScheduledRuns", "notifications__matrix__onScheduledRuns"),
        ("notifications:matrix:onBackfillRuns", "notifications__matrix__onBackfillRuns"),
        ("notifications:matrix:dashboardBaseUrl", "notifications__matrix__dashboardBaseUrl")
    ];

    public static IReadOnlyDictionary<string, string> Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in Mappings)
        {
            var value = configuration[mapping.ConfigurationKey];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            environment[mapping.EnvironmentKey] = value.Trim();
        }

        return environment;
    }
}