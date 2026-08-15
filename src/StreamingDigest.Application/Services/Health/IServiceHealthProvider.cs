using StreamingDigest.Domain.Health;

namespace StreamingDigest.Application.Services.Health;

/// <summary>
/// Service health probe interface. Each probe implementation checks a single service's operational status.
/// </summary>
public interface IServiceHealthProvider
{
    /// <summary>
    /// Execute health probe and return service status.
    /// </summary>
    Task<ServiceHealthDetails> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single service health probe.
/// </summary>
public sealed class ServiceHealthDetails
{
    /// <summary>
    /// Name of the service being probed (e.g., "API", "Worker", "Postgres").
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Current health state of the service.
    /// </summary>
    public required HealthState Status { get; init; }

    /// <summary>
    /// True if service failure blocks upgrade operations.
    /// </summary>
    public required bool IsRequired { get; init; }

    /// <summary>
    /// Timestamp of when this probe was executed.
    /// </summary>
    public DateTime LastCheck { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Latency of health check in milliseconds (null if timeout/error).
    /// </summary>
    public int? LatencyMs { get; init; }

    /// <summary>
    /// Number of consecutive retry attempts (for Reconnecting state).
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Additional probe-specific details (service version, capacity, etc.).
    /// </summary>
    public Dictionary<string, object?>? DetailJson { get; init; }

    /// <summary>
    /// Structured log output: service=X, status=Y, latency=Zms, retry=N
    /// </summary>
    public string DebugLog =>
        $"service={ServiceName}, status={Status}, latency={LatencyMs}ms, retry={RetryCount}";
}
