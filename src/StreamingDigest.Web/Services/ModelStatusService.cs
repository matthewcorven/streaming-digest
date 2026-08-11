using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

/// <summary>
/// Manages per-model row state for the Settings page. Responsibilities:
/// <list type="bullet">
///   <item>Loads the model catalog from <c>GET /api/models/options</c> and current runtime
///     state from <c>GET /api/models/status</c> to populate <see cref="Models"/>.</item>
///   <item>Subscribes to <c>GET /api/models/events</c> (SSE) and uses any received event
///     as a hint to re-fetch the authoritative status snapshot (per architecture note D5 —
///     SSE is in-process only; the status endpoint is the cross-process source of truth).</item>
///   <item>Exposes <see cref="Download"/> and <see cref="Verify"/> so the UI can initiate
///     operations and have row state updated atomically.</item>
///   <item>Raises <see cref="Changed"/> so the page can call StateHasChanged.</item>
/// </list>
/// </summary>
public sealed class ModelStatusService : IAsyncDisposable
{
    private readonly SearchUiSessionService _session;
    private readonly List<ModelRowViewModel> _models = [];
    private CancellationTokenSource? _sseCts;
    private Task? _sseTask;

    // ── Public surface ────────────────────────────────────────────────────────────────────

    /// <summary>Current per-model rows. Empty until <see cref="InitializeAsync"/> completes.</summary>
    public IReadOnlyList<ModelRowViewModel> Models => _models;

    /// <summary>Number of model operations currently in a running/queued state.</summary>
    public int ActiveOperationsCount =>
        _models.Count(m => m.RowState is ModelRowState.Submitting or
                                         ModelRowState.Queued or
                                         ModelRowState.Running or
                                         ModelRowState.Verifying);

    /// <summary>Current SSE connection state for the connection strip.</summary>
    public SseConnectionState ConnectionState { get; private set; } = SseConnectionState.Disconnected;

    /// <summary>Raised whenever any row state or the connection state changes.</summary>
    public event Action? Changed;

    public ModelStatusService(SearchUiSessionService session)
    {
        _session = session;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the model catalog + current status snapshot, then starts the SSE subscription.
    /// Safe to call multiple times; a second call rebuilds the row list and restarts SSE.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await StopSseAsync();

        await LoadSnapshotAsync(cancellationToken);
        StartSse();
        NotifyChanged();
    }

    /// <summary>
    /// Re-fetches <c>GET /api/models/status</c> and reconciles all rows (e.g. after SSE drop).
    /// Does not reset rows to Unknown — it overlays the snapshot on the current state.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileFromStatusEndpointAsync(cancellationToken);
        NotifyChanged();
    }

    // ── Actions ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates a model download for <paramref name="row"/>. Updates row state
    /// optimistically through Submitting → Queued, then fires-and-forgets SSE observation
    /// for further progress.
    /// </summary>
    public async Task<bool> Download(ModelRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (!row.TryBeginDownload())
        {
            return false;
        }

        NotifyChanged();

        try
        {
            await _session.EnsureAuthenticatedSessionAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/models/download");
            request.Content = JsonContent.Create(new { modelKind = row.Family, modelId = row.Id });

            using var response = await _session.SendAuthenticatedRequestAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await TryReadErrorDetailAsync(response, cancellationToken);
                row.ApplyDownloadFailed(detail);
                NotifyChanged();
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<DownloadAcceptedPayload>(
                JsonSerializerOptions.Default, cancellationToken);

            row.ApplyDownloadQueued(payload?.OperationId ?? Guid.Empty);
            NotifyChanged();
            return true;
        }
        catch (Exception ex)
        {
            row.ApplyDownloadFailed(ex.Message);
            NotifyChanged();
            return false;
        }
    }

    /// <summary>
    /// Initiates a verify probe for <paramref name="row"/>. Updates row state through
    /// Verifying → Ready or VerifyFailed.
    /// </summary>
    public async Task Verify(ModelRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (!row.TryBeginVerify())
        {
            return;
        }

        NotifyChanged();

        try
        {
            await _session.EnsureAuthenticatedSessionAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/models/verify");
            request.Content = JsonContent.Create(new { modelKind = row.Family, modelId = row.Id });

            using var response = await _session.SendAuthenticatedRequestAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await TryReadErrorDetailAsync(response, cancellationToken);
                row.ApplyVerifyFailed(detail);
                NotifyChanged();
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<VerifyPayload>(
                JsonSerializerOptions.Default, cancellationToken);

            if (payload?.Verified == true)
            {
                row.ApplyVerifyReady();
            }
            else
            {
                row.ApplyVerifyFailed(payload?.Message ?? "Verify returned not-ready.");
            }

            NotifyChanged();
        }
        catch (Exception ex)
        {
            row.ApplyVerifyFailed(ex.Message);
            NotifyChanged();
        }
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopSseAsync();
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────

    private async Task LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.EnsureAuthenticatedSessionAsync(cancellationToken);

            var optionsResponse = await _session.GetAuthenticatedJsonAsync<ModelsOptionsPayload>(
                "/api/models/options", cancellationToken);

            var statusResponse = await _session.GetAuthenticatedJsonAsync<ModelsStatusPayload>(
                "/api/models/status", cancellationToken);

            _models.Clear();

            var catalog = optionsResponse?.Models ?? [];
            var statuses = statusResponse?.Models ?? [];
            var statusByKey = statuses.ToDictionary(
                s => $"{s.Provider}:{s.ModelId}",
                s => s,
                StringComparer.OrdinalIgnoreCase);

            foreach (var option in catalog)
            {
                if (string.IsNullOrWhiteSpace(option.Id))
                {
                    continue;
                }

                var row = new ModelRowViewModel(
                    id: option.Id!,
                    label: option.Label ?? option.Id!,
                    provider: option.Provider ?? "ollama",
                    family: option.Family ?? "",
                    runtimeRole: option.RuntimeRole ?? "",
                    downloadable: option.Downloadable);

                // Apply the status snapshot if one exists.
                var key = $"{option.Provider}:{option.Id}";
                if (statusByKey.TryGetValue(key, out var state))
                {
                    row.ApplyStatusSnapshot(state.Status, state.ProgressPercent, state.LastErrorSummary);
                }

                _models.Add(row);
            }
        }
        catch
        {
            // A failed initial load leaves _models empty; the UI shows the error state.
            _models.Clear();
        }
    }

    private async Task ReconcileFromStatusEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.EnsureAuthenticatedSessionAsync(cancellationToken);

            var statusResponse = await _session.GetAuthenticatedJsonAsync<ModelsStatusPayload>(
                "/api/models/status", cancellationToken);

            var statuses = statusResponse?.Models ?? [];
            var statusByKey = statuses.ToDictionary(
                s => $"{s.Provider}:{s.ModelId}",
                s => s,
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in _models)
            {
                // Don't stomp on an in-flight user action (Submitting/Verifying).
                if (row.RowState is ModelRowState.Submitting or ModelRowState.Verifying)
                {
                    continue;
                }

                var key = $"{row.Provider}:{row.Id}";
                if (statusByKey.TryGetValue(key, out var state))
                {
                    row.ApplyStatusSnapshot(state.Status, state.ProgressPercent, state.LastErrorSummary);
                }
            }
        }
        catch
        {
            // Reconcile is best-effort; keep existing state on failure.
        }
    }

    private void StartSse()
    {
        _sseCts = new CancellationTokenSource();
        var token = _sseCts.Token;
        _sseTask = Task.Run(() => RunSseLoopAsync(token), token);
    }

    private async Task StopSseAsync()
    {
        if (_sseCts is not null)
        {
            await _sseCts.CancelAsync();
            if (_sseTask is not null)
            {
                try
                {
                    await _sseTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected.
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _sseCts.Dispose();
            _sseCts = null;
            _sseTask = null;
        }
    }

    private async Task RunSseLoopAsync(CancellationToken cancellationToken)
    {
        const int maxReconnectDelayMs = 16_000;
        var reconnectDelayMs = 500;
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetConnectionState(SseConnectionState.Connecting);

                await _session.EnsureAuthenticatedSessionAsync(cancellationToken);

                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/models/events");
                using var response = await _session.SendAuthenticatedRequestAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"SSE endpoint returned {(int)response.StatusCode}.");
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                SetConnectionState(SseConnectionState.Connected);
                consecutiveFailures = 0;
                reconnectDelayMs = 500;

                // Mark all paused rows as reconciling once we reconnect.
                foreach (var row in _models)
                {
                    if (row.RowState == ModelRowState.LiveUpdatesPaused)
                    {
                        // Will be overwritten by the reconcile below.
                        _ = row;
                    }
                }

                // Reconcile from the status endpoint now that we have a live stream.
                await ReconcileFromStatusEndpointAsync(cancellationToken);
                NotifyChanged();

                // Read the SSE stream. Any event received triggers another reconcile so the
                // UI reflects authoritative state (D5: SSE is hint-only, status is truth).
                await foreach (var evt in ReadSseEventsAsync(reader, cancellationToken))
                {
                    // SSE event received — re-fetch authoritative state.
                    await ReconcileFromStatusEndpointAsync(cancellationToken);
                    NotifyChanged();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Stream dropped or connection error.
                consecutiveFailures++;
            }

            // Mark all non-terminal rows as "paused" after a drop.
            foreach (var row in _models)
            {
                if (row.RowState is not (ModelRowState.Ready or
                                          ModelRowState.DownloadFailed or
                                          ModelRowState.VerifyFailed or
                                          ModelRowState.Submitting or
                                          ModelRowState.Verifying))
                {
                    row.ApplySseDropped();
                }
            }

            SetConnectionState(SseConnectionState.Reconnecting);
            NotifyChanged();

            // Stop reconnecting after 3 consecutive failures — degraded to manual refresh.
            if (consecutiveFailures >= 3)
            {
                SetConnectionState(SseConnectionState.Paused);
                NotifyChanged();
                return;
            }

            try
            {
                await Task.Delay(reconnectDelayMs, cancellationToken);
                reconnectDelayMs = Math.Min(reconnectDelayMs * 2, maxReconnectDelayMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetConnectionState(SseConnectionState.Disconnected);
    }

    private static async IAsyncEnumerable<SseEvent> ReadSseEventsAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? currentEvent = null;
        var dataBuilder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            // Null means EOF (stream closed by server).
            if (line is null)
            {
                yield break;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataBuilder.Append(line["data:".Length..].Trim());
            }
            else if (line.Length == 0)
            {
                // Dispatch
                if (currentEvent is not null || dataBuilder.Length > 0)
                {
                    yield return new SseEvent(currentEvent ?? "message", dataBuilder.ToString());
                }

                currentEvent = null;
                dataBuilder.Clear();
            }
            // Comment lines (starting with ':') are silently discarded.
        }
    }

    private void SetConnectionState(SseConnectionState state)
    {
        if (ConnectionState != state)
        {
            ConnectionState = state;
        }
    }

    private void NotifyChanged() => Changed?.Invoke();

    private static async Task<string?> TryReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorDetailPayload>(
                JsonSerializerOptions.Default, cancellationToken);
            return body?.Detail ?? body?.Title ?? $"HTTP {(int)response.StatusCode}";
        }
        catch
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    // ── Private payload records ───────────────────────────────────────────────────────────

    private sealed class ModelsOptionsPayload
    {
        public ModelOptionEntry[]? Models { get; set; }
    }

    private sealed class ModelOptionEntry
    {
        public string? Id { get; set; }
        public string? Family { get; set; }
        public string? Provider { get; set; }
        public string? RuntimeRole { get; set; }
        public bool Downloadable { get; set; }
        public string? Status { get; set; }
        public string? Label { get; set; }
    }

    private sealed class ModelsStatusPayload
    {
        public ModelStatusEntry[]? Models { get; set; }
    }

    private sealed class ModelStatusEntry
    {
        public string? Provider { get; set; }
        public string? ModelId { get; set; }
        public string? RuntimeRole { get; set; }
        public string? Status { get; set; }
        public Guid? CurrentOperationId { get; set; }
        public int? ProgressPercent { get; set; }
        public DateTimeOffset? LastVerifiedAt { get; set; }
        public string? LastErrorSummary { get; set; }
    }

    private sealed class DownloadAcceptedPayload
    {
        public string? Status { get; set; }
        public Guid OperationId { get; set; }
        public string? StatusUrl { get; set; }
        public string? ModelId { get; set; }
    }

    private sealed class VerifyPayload
    {
        public string? Status { get; set; }
        public bool Verified { get; set; }
        public string? Message { get; set; }
    }

    private sealed class ErrorDetailPayload
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
    }

    private sealed record SseEvent(string Name, string Data);
}

/// <summary>State of the SSE stream connection for the Settings page connection strip.</summary>
public enum SseConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Paused
}
