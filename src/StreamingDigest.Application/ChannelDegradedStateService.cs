using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// The channel-level state transition that occurred when an adapter failure was recorded.
/// </summary>
public enum ChannelDegradedTransition
{
    /// <summary>No state change (channel already degraded or counter was not affected).</summary>
    None,

    /// <summary>
    /// The consecutive-failure count was incremented but the degraded threshold was
    /// not yet reached.
    /// </summary>
    ConsecutiveFailureIncremented,

    /// <summary>
    /// The channel crossed the failure threshold and entered the <em>Degraded</em> state.
    /// Callers should emit a <c>channel_degraded_entered</c> domain event.
    /// </summary>
    DegradedEntered,

    /// <summary>
    /// An active rate-limit deferment was in effect; the failure counter was not
    /// incremented (ADR-0003: failures during a deferment don't count).
    /// </summary>
    CounterPausedByDeferment,
}

/// <summary>
/// Pure, stateless service that encapsulates all state-transition logic for the
/// <em>Degraded</em> channel lifecycle defined in ADR-0003.
/// <para>
/// Methods mutate the <see cref="Channel"/> domain object in-place.
/// No persistence: the caller is responsible for saving the updated channel via the
/// repository after a transition.
/// </para>
/// </summary>
public static class ChannelDegradedStateService
{
    /// <summary>
    /// Number of consecutive adapter-stage run failures required to enter the Degraded state
    /// (ADR-0003: "after two consecutive runs fail at the adapter stage").
    /// </summary>
    public const int DegradedFailureThreshold = 2;

    /// <summary>
    /// Returns <c>true</c> when the channel should be skipped for full ingestion processing
    /// and instead receive only a lightweight probe this run.
    /// <para>
    /// Channel-state precedence (DATA_MODEL §3.5): Paused channels get no selection,
    /// no probing, and no failure counting.  Only non-Paused, Degraded channels probe.
    /// </para>
    /// </summary>
    public static bool ShouldSkipForFullIngestion(Channel channel)
        => channel.IsDegraded && !channel.IsPaused;

    /// <summary>
    /// Returns <c>true</c> when a lightweight probe should be performed for this channel
    /// this run.  Identical condition to <see cref="ShouldSkipForFullIngestion"/> — the
    /// same channels that skip full ingestion receive exactly one probe.
    /// </summary>
    public static bool ShouldProbe(Channel channel)
        => channel.IsDegraded && !channel.IsPaused;

    /// <summary>
    /// Records an adapter-stage failure for the channel and applies the appropriate
    /// state transition.
    /// </summary>
    /// <param name="channel">The channel whose run just failed at the adapter stage.</param>
    /// <param name="hasDeferment">
    /// <c>true</c> when an active rate-limit deferment exists for this channel's host.
    /// When <c>true</c>, the failure counter is not incremented (ADR-0003: failures during
    /// a deferment don't count — the channel never had a fair chance).
    /// </param>
    /// <param name="now">The reference timestamp (UTC) for recording <c>degraded_at</c>.</param>
    /// <returns>The transition that was applied.</returns>
    public static ChannelDegradedTransition RecordAdapterFailure(
        Channel channel,
        bool hasDeferment,
        DateTimeOffset now)
    {
        if (hasDeferment)
        {
            return ChannelDegradedTransition.CounterPausedByDeferment;
        }

        channel.ConsecutiveFailures++;

        if (!channel.IsDegraded && channel.ConsecutiveFailures >= DegradedFailureThreshold)
        {
            channel.IsDegraded = true;
            channel.DegradedAt = now;
            return ChannelDegradedTransition.DegradedEntered;
        }

        return channel.IsDegraded
            ? ChannelDegradedTransition.None
            : ChannelDegradedTransition.ConsecutiveFailureIncremented;
    }

    /// <summary>
    /// Records a successful probe for a Degraded channel.
    /// Clears the Degraded state and resets the consecutive-failure counter so the
    /// channel rejoins the normal ingestion pool on the next run.
    /// Callers should emit a <c>channel_probe_succeeded</c> domain event.
    /// </summary>
    public static void RecordSuccessfulProbe(Channel channel, DateTimeOffset now)
    {
        channel.IsDegraded = false;
        channel.ConsecutiveFailures = 0;
        channel.DegradedAt = null;
        channel.LastProbeAt = now;
    }

    /// <summary>
    /// Records a failed probe for a Degraded channel.
    /// Increments the consecutive-failure counter and updates the last-probe timestamp.
    /// The channel remains Degraded; the next scheduled run will probe again.
    /// Callers should emit a <c>channel_probe_failed</c> domain event.
    /// </summary>
    public static void RecordFailedProbe(Channel channel, DateTimeOffset now)
    {
        channel.ConsecutiveFailures++;
        channel.LastProbeAt = now;
    }

    /// <summary>
    /// Applies a user-initiated manual clear of the Degraded state.
    /// Resets the consecutive-failure counter and clears <c>is_degraded</c> so the
    /// channel re-enters the active ingestion pool on the next run.
    /// <para>
    /// Per ADR-0003: "this only resets the counter, so the next run re-trips it if the
    /// underlying problem persists."  The caller does not need to persist
    /// <c>degraded_at</c> as a history entry — the domain-event log captures the clear.
    /// </para>
    /// </summary>
    public static void ClearDegradedManually(Channel channel)
    {
        channel.IsDegraded = false;
        channel.ConsecutiveFailures = 0;
        channel.DegradedAt = null;
    }
}
