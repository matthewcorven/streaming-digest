namespace StreamingDigest.Web.Models;

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

/// <summary>
/// Service health status for API responses and database tracking.
/// Provides clear operational truth for services and components.
/// </summary>
public enum ServiceHealthStatus
{
    /// <summary>Service is fully operational with no issues.</summary>
    Healthy = 0,

    /// <summary>Service is operational but showing warnings or performance issues.</summary>
    Degraded = 1,

    /// <summary>Service has warnings but remains operational; requires attention.</summary>
    Warning = 2,

    /// <summary>Service has encountered a critical error and is not operational.</summary>
    Error = 3,

    /// <summary>Service health status is unknown (not yet checked or last check failed).</summary>
    Unknown = 4
}
