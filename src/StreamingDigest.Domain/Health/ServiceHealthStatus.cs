namespace StreamingDigest.Domain.Health;

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
