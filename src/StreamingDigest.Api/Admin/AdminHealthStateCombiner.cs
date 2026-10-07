using StreamingDigest.Domain.Health;

namespace StreamingDigest.Api.Admin;

public static class AdminHealthStateCombiner
{
    public static HealthState MergeOverallHealth(HealthState compositeOverallHealth, HealthState matrixHealth)
    {
        if (compositeOverallHealth == HealthState.Error || matrixHealth == HealthState.Error)
        {
            return HealthState.Error;
        }

        if (compositeOverallHealth == HealthState.Reconnecting || matrixHealth == HealthState.Reconnecting)
        {
            return HealthState.Reconnecting;
        }

        if (compositeOverallHealth == HealthState.Paused || matrixHealth == HealthState.Paused)
        {
            return HealthState.Paused;
        }

        if (compositeOverallHealth == HealthState.Degraded || matrixHealth == HealthState.Degraded)
        {
            return HealthState.Degraded;
        }

        return HealthState.Ready;
    }
}