using Microsoft.Extensions.Configuration;
using StreamingDigest.AppHost;

namespace StreamingDigest.UnitTests;

public sealed class MatrixNotificationEnvironmentTests
{
    [Fact]
    public void Build_returns_only_configured_matrix_environment_variables()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Matrix:Enabled"] = "true",
                ["Notifications:Matrix:AccessToken"] = "secret-token",
                ["Notifications:Matrix:RoomId"] = "!room:example.com",
                ["Notifications:Matrix:OnBackfillRuns"] = "false"
            })
            .Build();

        var environment = MatrixNotificationEnvironment.Build(configuration);

        Assert.Equal("true", environment["notifications__matrix__enabled"]);
        Assert.Equal("secret-token", environment["notifications__matrix__accessToken"]);
        Assert.Equal("!room:example.com", environment["notifications__matrix__roomId"]);
        Assert.Equal("false", environment["notifications__matrix__onBackfillRuns"]);
        Assert.DoesNotContain("notifications__matrix__homeserverUrl", environment.Keys);
    }
}