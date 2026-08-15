namespace StreamingDigest.Domain.Health;

/// <summary>
/// Represents the operational health state of the system or a component.
/// Used as the single source of truth for both API responses and test assertions.
/// </summary>
public enum HealthState
{
    /// <summary>System or component is fully operational with no issues.</summary>
    Ready = 0,

    /// <summary>System or component is operational but with warnings or degraded performance.</summary>
    Degraded = 1,

    /// <summary>System or component is actively attempting to recover from a connectivity issue.</summary>
    Reconnecting = 2,

    /// <summary>System or component is paused by administrator action (e.g., maintenance mode).</summary>
    Paused = 3,

    /// <summary>System or component has encountered a critical error and is not operational.</summary>
    Error = 4
}
