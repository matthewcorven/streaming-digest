using StreamingDigest.Api.Admin;
using StreamingDigest.Domain.Health;

namespace StreamingDigest.UnitTests;

public sealed class AdminHealthStateCombinerTests
{
    [Theory]
    [InlineData(HealthState.Ready, HealthState.Ready, HealthState.Ready)]
    [InlineData(HealthState.Ready, HealthState.Degraded, HealthState.Degraded)]
    [InlineData(HealthState.Degraded, HealthState.Ready, HealthState.Degraded)]
    [InlineData(HealthState.Ready, HealthState.Reconnecting, HealthState.Reconnecting)]
    [InlineData(HealthState.Paused, HealthState.Ready, HealthState.Paused)]
    [InlineData(HealthState.Ready, HealthState.Error, HealthState.Error)]
    public void MergeOverallHealth_returns_the_stricter_state(
        HealthState compositeOverallHealth,
        HealthState matrixHealth,
        HealthState expected)
    {
        var actual = AdminHealthStateCombiner.MergeOverallHealth(compositeOverallHealth, matrixHealth);

        Assert.Equal(expected, actual);
    }
}