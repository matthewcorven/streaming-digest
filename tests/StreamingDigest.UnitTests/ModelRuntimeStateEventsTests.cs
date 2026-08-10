using System.Text.Json;
using StreamingDigest.Application.Models;

namespace StreamingDigest.UnitTests;

public sealed class ModelRuntimeStateEventsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FromStateTransition_non_terminal_status_emits_model_status_event()
    {
        var state = CreateState(status: "running", progressPercent: 42);

        var modelEvent = ModelRuntimeStateEvents.FromStateTransition(state);

        Assert.Equal("model.status", modelEvent.Name);
        using var payload = JsonDocument.Parse(modelEvent.DataJson);
        Assert.Equal("ollama", payload.RootElement.GetProperty("provider").GetString());
        Assert.Equal("bge-m3", payload.RootElement.GetProperty("modelId").GetString());
        Assert.Equal("embedding", payload.RootElement.GetProperty("runtimeRole").GetString());
        Assert.Equal("running", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(42, payload.RootElement.GetProperty("progressPercent").GetInt32());
    }

    [Fact]
    public void FromStateTransition_ready_with_operation_emits_operation_completed_event()
    {
        var operationId = Guid.NewGuid();
        var state = CreateState(status: "ready", currentOperationId: operationId);

        var modelEvent = ModelRuntimeStateEvents.FromStateTransition(state);

        Assert.Equal("operation.completed", modelEvent.Name);
        using var payload = JsonDocument.Parse(modelEvent.DataJson);
        Assert.Equal(operationId, payload.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("bge-m3", payload.RootElement.GetProperty("modelId").GetString());
    }

    [Fact]
    public void FromStateTransition_failed_with_operation_emits_operation_failed_event_with_error()
    {
        var operationId = Guid.NewGuid();
        var state = CreateState(status: "failed", currentOperationId: operationId, lastErrorSummary: "pull stalled");

        var modelEvent = ModelRuntimeStateEvents.FromStateTransition(state);

        Assert.Equal("operation.failed", modelEvent.Name);
        using var payload = JsonDocument.Parse(modelEvent.DataJson);
        Assert.Equal(operationId, payload.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("pull stalled", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void FromStateTransition_ready_without_operation_stays_a_model_status_event()
    {
        var state = CreateState(status: "ready", currentOperationId: null);

        var modelEvent = ModelRuntimeStateEvents.FromStateTransition(state);

        Assert.Equal("model.status", modelEvent.Name);
    }

    [Fact]
    public void CreateOperationStatusEvent_serializes_progress_fields()
    {
        var operationId = Guid.NewGuid();

        var modelEvent = ModelRuntimeStateEvents.CreateOperationStatusEvent(
            operationId,
            "ollama",
            "llama3.1:8b",
            "running",
            progressPercent: 77,
            timestamp: new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("operation.status", modelEvent.Name);
        using var payload = JsonDocument.Parse(modelEvent.DataJson);
        Assert.Equal(operationId, payload.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("running", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(77, payload.RootElement.GetProperty("progressPercent").GetInt32());
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero), modelEvent.Timestamp);
    }

    private static ModelRuntimeState CreateState(
        string status,
        Guid? currentOperationId = null,
        int? progressPercent = null,
        string? lastErrorSummary = null)
    {
        return new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "embedding",
            Status = status,
            CurrentOperationId = currentOperationId,
            ProgressPercent = progressPercent,
            LastErrorSummary = lastErrorSummary,
            UpdatedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)
        };
    }
}
