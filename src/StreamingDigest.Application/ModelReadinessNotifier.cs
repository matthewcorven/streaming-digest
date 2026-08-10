using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Shared WS-7 notification helper. When a runtime seam degrades because a model is unready,
/// it reports through this single helper instead of every seam re-implementing its own signal
/// (plan §8: "one shared notification helper"). The helper always logs a structured warning;
/// when a <see cref="IStreamingDigestDbContext"/> is supplied it also emits a
/// <c>model_readiness_degraded</c> <see cref="DomainEvent"/> (operator-visible in-app signal).
///
/// Emission is once per (seam, provider, modelId) per process to avoid spamming logs / the
/// domain-event log on hot paths (search, refine, classify) while the model stays unready.
/// </summary>
public interface IModelReadinessNotifier
{
    /// <summary>
    /// Report that a seam degraded because a model was unready. Returns <c>true</c> when the
    /// report was emitted now, <c>false</c> when suppressed as a duplicate or the model is ready.
    /// </summary>
    bool ReportDegraded(
        string seam,
        ModelReadiness readiness,
        string action,
        IStreamingDigestDbContext? dbContext = null,
        string? entityType = null,
        Guid? entityId = null);
}

public sealed class ModelReadinessNotifier : IModelReadinessNotifier
{
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ModelReadinessNotifier> _logger;

    public ModelReadinessNotifier(ILogger<ModelReadinessNotifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool ReportDegraded(
        string seam,
        ModelReadiness readiness,
        string action,
        IStreamingDigestDbContext? dbContext = null,
        string? entityType = null,
        Guid? entityId = null)
    {
        if (readiness is null)
        {
            throw new ArgumentNullException(nameof(readiness));
        }

        if (readiness.IsReady)
        {
            return false;
        }

        var key = $"{seam}|{readiness.Provider}|{readiness.ModelId}";
        if (!_reported.TryAdd(key, 0))
        {
            return false;
        }

        var message = $"[WS-7 {seam}] {readiness.Reason} Action: {action}.";
        _logger.LogWarning(
            "WS-7 seam {Seam} degraded: {Reason} Action: {Action} Provider={Provider} Model={ModelId}",
            seam, readiness.Reason, action, readiness.Provider, readiness.ModelId);

        dbContext?.DomainEvents.Add(new DomainEvent
        {
            Id = Guid.NewGuid(),
            EventType = DomainEventTypeCatalog.RequireDefined(DomainEventTypeCatalog.ModelReadinessDegraded),
            Severity = "warning",
            EntityType = entityType,
            EntityId = entityId,
            Message = message,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                seam,
                role = readiness.Role.ToString(),
                provider = readiness.Provider,
                modelId = readiness.ModelId,
                status = readiness.Status,
                reason = readiness.Reason,
                action
            }),
            CreatedAt = DateTimeOffset.UtcNow
        });

        return true;
    }
}
