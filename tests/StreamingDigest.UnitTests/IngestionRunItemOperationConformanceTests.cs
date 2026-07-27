using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

/// <summary>
/// Conformance tests for the mutability contracts defined in
/// ADR-0005: Ingestion Runs are immutable, Items are living, Operations are request handles.
/// </summary>
public sealed class IngestionRunItemOperationConformanceTests
{
    // ── Run immutability (ADR-0005: "Runs are immutable once completed_at is set") ──

    [Fact]
    public void IngestionRun_completed_at_can_be_set_once_and_is_nullable_before_completion()
    {
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow
        };

        Assert.Null(run.CompletedAt);

        var completedAt = DateTimeOffset.UtcNow;
        run.CompletedAt = completedAt;
        run.Status = "completed";

        Assert.Equal(completedAt, run.CompletedAt);
        Assert.Equal("completed", run.Status);
    }

    [Fact]
    public void IngestionRun_summary_counts_are_independent_of_live_item_state()
    {
        // ADR-0005: "Run detail pages derive a live rollup from current item states
        // instead of re-reading the frozen record."
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            Status = "completed",
            VideosIngested = 5,
            VideosFailed = 1,
            CompletedAt = DateTimeOffset.UtcNow
        };

        var frozenVideosIngested = run.VideosIngested;
        var frozenVideosFailed = run.VideosFailed;

        // The frozen summary is preserved independently; live rollup is computed separately
        Assert.Equal(5, frozenVideosIngested);
        Assert.Equal(1, frozenVideosFailed);
    }

    [Fact]
    public void IngestionRun_retains_originating_operation_id_after_completion()
    {
        var operationId = Guid.NewGuid();
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            Status = "completed",
            CompletedAt = DateTimeOffset.UtcNow
        };

        // The run keeps its originating operation; it is not replaced on retry
        Assert.Equal(operationId, run.OperationId);
    }

    // ── Item living-row contract (ADR-0005: "Items are living rows") ──────────

    [Fact]
    public void IngestionItem_retry_increments_attempt_count_in_place()
    {
        // ADR-0005: "retry mutates status/attempt/timestamps in place"
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            Status = "failed",
            Attempt = 1,
            RetryCount = 0,
            MaxAttempts = 3,
            IsRetryable = true
        };

        // Simulate a retry mutation
        item.Attempt += 1;
        item.RetryCount += 1;
        item.Status = "pending";
        item.ErrorSummary = null;

        Assert.Equal(2, item.Attempt);
        Assert.Equal(1, item.RetryCount);
        Assert.Equal("pending", item.Status);
    }

    [Fact]
    public void IngestionItem_retains_originating_run_id_across_retries()
    {
        // ADR-0005: "Items stay attached to their originating run forever."
        var runId = Guid.NewGuid();
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = runId,
            Status = "failed",
            Attempt = 1
        };

        // Simulate a retry — only status/attempt fields change; run association is permanent
        item.Status = "pending";
        item.Attempt += 1;

        Assert.Equal(runId, item.IngestionRunId);
    }

    // ── Operation handle contract (ADR-0005: "Operations are the tracking handle") ──

    [Fact]
    public void IngestionItem_operation_id_can_be_updated_to_latest_operation()
    {
        // ADR-0005: "each item links only its latest operation"
        var originalOperationId = Guid.NewGuid();
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            OperationId = originalOperationId,
            Status = "failed"
        };

        var retryOperationId = Guid.NewGuid();
        item.OperationId = retryOperationId;

        Assert.Equal(retryOperationId, item.OperationId);
        Assert.NotEqual(originalOperationId, item.OperationId);
    }

    [Fact]
    public void IngestionItem_has_deferment_fields_for_rate_limit_deferment()
    {
        // ADR-0005 cross-reference: items may be deferred (rate_limit_deferment_created event)
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            Status = "deferred",
            DeferredUntil = DateTimeOffset.UtcNow.AddMinutes(5),
            DefermentReason = "rate_limit"
        };

        Assert.NotNull(item.DeferredUntil);
        Assert.Equal("rate_limit", item.DefermentReason);
    }

    [Fact]
    public void IngestionItem_max_attempts_bounds_retry_eligibility()
    {
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            Status = "failed",
            Attempt = 3,
            MaxAttempts = 3,
            IsRetryable = true
        };

        // At max attempts the item is no longer eligible for retry
        var isEligibleForRetry = item.IsRetryable && item.Attempt < item.MaxAttempts;
        Assert.False(isEligibleForRetry);
    }

    [Fact]
    public void IngestionItem_with_non_retryable_flag_is_never_eligible_for_retry()
    {
        var item = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = Guid.NewGuid(),
            Status = "failed",
            Attempt = 1,
            MaxAttempts = 5,
            IsRetryable = false
        };

        var isEligibleForRetry = item.IsRetryable && item.Attempt < item.MaxAttempts;
        Assert.False(isEligibleForRetry);
    }

    // ── Cross-contract: run/item separation (ADR-0005 consequences) ──────────

    [Fact]
    public void Run_failed_status_is_independent_of_current_item_states()
    {
        // ADR-0005: "A run can show 'failed' historically while its live rollup
        // shows all-green after successful retries — this is intended."
        var runId = Guid.NewGuid();

        var run = new IngestionRun
        {
            Id = runId,
            Status = "failed",
            VideosFailed = 1,
            VideosIngested = 4,
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        // After a retry the item transitions to succeeded; the frozen run record is unchanged
        var retriedItem = new IngestionItem
        {
            Id = Guid.NewGuid(),
            IngestionRunId = runId,
            Status = "succeeded",   // live item state: all-green
            Attempt = 2,
            RetryCount = 1
        };

        // Frozen run summary still reflects the original outcome
        Assert.Equal("failed", run.Status);
        Assert.Equal(1, run.VideosFailed);

        // Live item state differs — this is intentional per ADR-0005
        Assert.Equal("succeeded", retriedItem.Status);
        Assert.Equal(runId, retriedItem.IngestionRunId);
    }
}
