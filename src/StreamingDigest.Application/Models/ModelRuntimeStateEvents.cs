using System.Text.Json;

namespace StreamingDigest.Application.Models;

/// <summary>
/// Factory helpers that translate persisted model-runtime state transitions into the SSE
/// event contract owned by <c>GET /api/models/events</c> (model-download implementation
/// plan §6). Publishers (download job, verify endpoint) call these so event naming and
/// payload shape stay centralized.
/// </summary>
public static class ModelRuntimeStateEvents
{
    public const string ModelStatusEventName = "model.status";
    public const string OperationStatusEventName = "operation.status";
    public const string OperationCompletedEventName = "operation.completed";
    public const string OperationFailedEventName = "operation.failed";

    /// <summary>
    /// Builds the <c>model.status</c> event for a persisted <see cref="ModelRuntimeState"/>
    /// transition. Emits <c>operation.completed</c>/<c>operation.failed</c> instead when the
    /// state reaches a terminal status with an attached operation.
    /// </summary>
    public static ModelLifecycleEvent FromStateTransition(ModelRuntimeState state, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var effectiveTimestamp = timestamp ?? state.UpdatedAt;

        if (string.Equals(state.Status, "ready", StringComparison.OrdinalIgnoreCase)
            && state.CurrentOperationId is Guid completedOperationId)
        {
            return new ModelLifecycleEvent(
                OperationCompletedEventName,
                Serialize(new { operationId = completedOperationId, provider = state.Provider, modelId = state.ModelId }),
                effectiveTimestamp);
        }

        if (string.Equals(state.Status, "failed", StringComparison.OrdinalIgnoreCase)
            && state.CurrentOperationId is Guid failedOperationId)
        {
            return new ModelLifecycleEvent(
                OperationFailedEventName,
                Serialize(new { operationId = failedOperationId, provider = state.Provider, modelId = state.ModelId, error = state.LastErrorSummary }),
                effectiveTimestamp);
        }

        return new ModelLifecycleEvent(
            ModelStatusEventName,
            Serialize(new
            {
                provider = state.Provider,
                modelId = state.ModelId,
                runtimeRole = state.RuntimeRole,
                status = state.Status,
                progressPercent = state.ProgressPercent,
                currentOperationId = state.CurrentOperationId,
                lastVerifiedAt = state.LastVerifiedAt,
                lastErrorSummary = state.LastErrorSummary,
                updatedAt = state.UpdatedAt
            }),
            effectiveTimestamp);
    }

    /// <summary>
    /// Builds the <c>operation.status</c> progress event for an in-flight operation.
    /// </summary>
    public static ModelLifecycleEvent CreateOperationStatusEvent(
        Guid operationId,
        string provider,
        string modelId,
        string status,
        int? progressPercent = null,
        string? error = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        return new ModelLifecycleEvent(
            OperationStatusEventName,
            Serialize(new { operationId, provider, modelId, status, progressPercent, error }),
            timestamp ?? DateTimeOffset.UtcNow);
    }

    private static string Serialize<TValue>(TValue value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
