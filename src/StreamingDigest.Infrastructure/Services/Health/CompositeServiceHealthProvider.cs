using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Services.Health;
using StreamingDigest.Domain.Health;

namespace StreamingDigest.Infrastructure.Services.Health;

/// <summary>
/// Orchestrates health probes across all services in parallel.
/// Stub implementation; Tank #271 will implement concrete probes.
/// </summary>
public sealed class CompositeServiceHealthProvider
{
    private readonly ILogger _logger;

    public CompositeServiceHealthProvider(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute all health probes in parallel with per-probe timeout.
    /// Stub: returns Ready state for all services.
    /// </summary>
    public async Task<ServiceHealthDetails[]> ProbeAllAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("CompositeServiceHealthProvider.ProbeAllAsync: stub implementation");
        
        // Stub: return empty array; Tank #271 will implement real probes
        return [];
    }

    /// <summary>
    /// Compute overall health state from individual probe results.
    /// Stub: returns Ready.
    /// </summary>
    public HealthState ComputeOverallHealth(ServiceHealthDetails[] probes)
    {
        _logger.LogDebug("ComputeOverallHealth: stub implementation, returning Ready");
        return HealthState.Ready;
    }
}
