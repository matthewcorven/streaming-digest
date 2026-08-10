using System.Text.Json;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Worker.ModelDownload;

/// <summary>
/// Worker-owned execution pipeline for model downloads. A single reader loop drains the
/// bounded channel (pull concurrency 1), streams the Ollama pull via
/// <see cref="IModelRuntimeClient.PullModelAsync"/>, and transitions
/// <c>model_runtime_state</c> queued → running → ready|failed while mirroring operation
/// status into the <c>operations</c> table. Failures send a best-effort Matrix notification.
/// </summary>
public sealed class ModelDownloadHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions SummaryJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ChannelModelDownloadQueue _queue;
    private readonly IModelRuntimeClient _runtimeClient;
    private readonly IModelRuntimeStateRepository _stateRepository;
    private readonly IOperationStore _operationStore;
    private readonly AppReadinessStateService _readinessStateService;
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModelDownloadHostedService> _logger;

    public ModelDownloadHostedService(
        ChannelModelDownloadQueue queue,
        IModelRuntimeClient runtimeClient,
        IModelRuntimeStateRepository stateRepository,
        IOperationStore operationStore,
        AppReadinessStateService readinessStateService,
        string connectionString,
        IServiceProvider serviceProvider,
        ILogger<ModelDownloadHostedService> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _runtimeClient = runtimeClient ?? throw new ArgumentNullException(nameof(runtimeClient));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        _readinessStateService = readinessStateService ?? throw new ArgumentNullException(nameof(readinessStateService));
        _connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Model download hosted service started; pull concurrency is 1.");

        await foreach (var command in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteDownloadAsync(command, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown: leave the entry marked failed so the UI does not show a
                // phantom "running" forever. Best effort only.
                await TryMarkFailedAsync(
                    command,
                    "The worker shut down while the model download was running.",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled model download pipeline error (operation {OperationId}, {Provider}/{ModelId}).",
                    command.OperationId,
                    command.Provider,
                    command.ModelId);

                await TryMarkFailedAsync(command, ex.Message, CancellationToken.None);
            }
            finally
            {
                _queue.Complete(command);
            }
        }

        _logger.LogInformation("Model download hosted service stopped.");
    }

    private async Task ExecuteDownloadAsync(ModelDownloadCommand command, CancellationToken cancellationToken)
    {
        if (!string.Equals(command.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            await TryMarkFailedAsync(command, $"Provider '{command.Provider}' does not support downloads.", cancellationToken);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;

        // queued -> running
        await UpsertStateAsync(command, ModelDownloadStatuses.Running, startedAt, progressPercent: null, errorSummary: null, details: null, cancellationToken);
        await PersistOperationTransitionAsync(command, "running", startedAt: startedAt, completedAt: null, errorSummary: null, summary: BuildSummary(command, "running", null, null), cancellationToken);

        var lastReportedProgress = -1;
        try
        {
            await foreach (var progress in _runtimeClient.PullModelAsync(command.ModelId, stream: true, cancellationToken))
            {
                var percent = progress.Percent ?? -1;
                if (percent != lastReportedProgress)
                {
                    lastReportedProgress = percent;
                    await UpsertStateAsync(
                        command,
                        ModelDownloadStatuses.Running,
                        startedAt,
                        progress.Percent,
                        errorSummary: null,
                        details: new Dictionary<string, object?>
                        {
                            ["stage"] = progress.Status,
                            ["totalBytes"] = progress.Total,
                            ["completedBytes"] = progress.Completed
                        },
                        cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(command, startedAt, $"Model pull failed: {ex.Message}", CancellationToken.None);
            return;
        }

        // Presence confirmation: the runtime (not the DB) is the source of truth.
        IReadOnlyList<ModelPresence> installed;
        try
        {
            installed = await _runtimeClient.ListInstalledModelsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(command, startedAt, $"Model presence check failed after pull: {ex.Message}", CancellationToken.None);
            return;
        }

        var presence = installed.FirstOrDefault(model => ModelIdMatches(model.ModelId, command.ModelId));
        if (presence is null)
        {
            await FailAsync(command, startedAt, $"Model '{command.ModelId}' did not appear in the Ollama runtime after the pull completed.", CancellationToken.None);
            return;
        }

        // Terminal-state writes are host-lifecycle-critical: use CancellationToken.None so a
        // StopAsync racing these writes cannot cancel them mid-flight and let the shutdown
        // handler clobber the real outcome with "worker shut down".
        var completedAt = DateTimeOffset.UtcNow;
        await UpsertStateAsync(
            command,
            ModelDownloadStatuses.Ready,
            completedAt,
            progressPercent: 100,
            errorSummary: null,
            details: new Dictionary<string, object?>
            {
                ["digest"] = presence.Digest,
                ["sizeBytes"] = presence.SizeInBytes
            },
            CancellationToken.None);
        await PersistOperationTransitionAsync(
            command,
            "completed",
            startedAt: startedAt,
            completedAt: completedAt,
            errorSummary: null,
            summary: BuildSummary(command, "ready", presence.Digest, presence.SizeInBytes),
            CancellationToken.None);

        // Readiness projection: only real verified presence may mark the step succeeded (WS-4 / §4.1).
        await ProjectReadinessAsync(command, presence, CancellationToken.None);

        _logger.LogInformation(
            "Model download completed (operation {OperationId}, {Provider}/{ModelId}, digest {Digest}).",
            command.OperationId,
            command.Provider,
            command.ModelId,
            presence.Digest);
    }

    private async Task FailAsync(ModelDownloadCommand command, DateTimeOffset startedAt, string errorSummary, CancellationToken cancellationToken)
    {
        var failedAt = DateTimeOffset.UtcNow;
        await UpsertStateAsync(command, ModelDownloadStatuses.Failed, failedAt, progressPercent: null, errorSummary: errorSummary, details: null, cancellationToken);
        await PersistOperationTransitionAsync(command, "failed", startedAt: startedAt, completedAt: failedAt, errorSummary: errorSummary, summary: BuildSummary(command, "failed", null, null), cancellationToken);

        _logger.LogError(
            "Model download failed (operation {OperationId}, {Provider}/{ModelId}): {ErrorSummary}",
            command.OperationId,
            command.Provider,
            command.ModelId,
            errorSummary);

        await NotifyFailureAsync(command, errorSummary);
    }

    private async Task TryMarkFailedAsync(ModelDownloadCommand command, string errorSummary, CancellationToken cancellationToken)
    {
        try
        {
            await FailAsync(command, DateTimeOffset.UtcNow, errorSummary, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist the failure state for model download (operation {OperationId}, {Provider}/{ModelId}).",
                command.OperationId,
                command.Provider,
                command.ModelId);
        }
    }

    private async Task UpsertStateAsync(
        ModelDownloadCommand command,
        string status,
        DateTimeOffset updatedAt,
        int? progressPercent,
        string? errorSummary,
        IReadOnlyDictionary<string, object?>? details,
        CancellationToken cancellationToken)
    {
        await _stateRepository.UpsertAsync(
            new ModelRuntimeState
            {
                Id = Guid.NewGuid(),
                Provider = command.Provider,
                ModelId = command.ModelId,
                RuntimeRole = command.RuntimeRole,
                Status = status,
                CurrentOperationId = command.OperationId,
                ProgressPercent = progressPercent,
                LastErrorSummary = errorSummary,
                DetailsJson = details is null ? null : JsonSerializer.Serialize(details, SummaryJsonOptions),
                UpdatedAt = updatedAt
            },
            cancellationToken);
    }

    private async Task PersistOperationTransitionAsync(
        ModelDownloadCommand command,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        string? errorSummary,
        string summary,
        CancellationToken cancellationToken)
    {
        var existing = await _operationStore.GetByIdAsync(command.OperationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        await _operationStore.PersistAsync(
            new OperationRecord
            {
                Id = command.OperationId,
                OperationType = existing?.OperationType ?? "model.download",
                Status = status,
                RiskLevel = existing?.RiskLevel,
                RequestedBy = existing?.RequestedBy,
                RelatedEntityType = existing?.RelatedEntityType,
                RelatedEntityId = existing?.RelatedEntityId,
                HangfireJobId = existing?.HangfireJobId,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                SummaryJson = summary,
                ErrorSummary = errorSummary,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            },
            cancellationToken);
    }

    private async Task ProjectReadinessAsync(ModelDownloadCommand command, ModelPresence presence, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, object?>
        {
            ["provider"] = command.Provider,
            ["modelId"] = command.ModelId,
            ["runtimeRole"] = command.RuntimeRole,
            ["operationId"] = command.OperationId,
            ["digest"] = presence.Digest,
            ["sizeBytes"] = presence.SizeInBytes,
            ["verifiedPresent"] = true
        };

        // Role-level onboarding steps (plan S9): embedding_model_verified / llm_model_verified /
        // audio_to_text_verified. Written only after real presence is confirmed, matching the
        // step-key convention used by ModelDiscoveryService.
        var stepKey = command.RuntimeRole switch
        {
            "embedding" => "embedding_model_verified",
            "llm" => "llm_model_verified",
            "audio" => "audio_to_text_verified",
            _ => $"{command.RuntimeRole}_model_verified"
        };

        try
        {
            await _readinessStateService.VerifyStepAsync(
                _connectionString,
                stepKey,
                new OnboardingStepVerificationRequest(
                    Status: "succeeded",
                    Details: details),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Readiness projection failed after successful model download (operation {OperationId}, {Provider}/{ModelId}).",
                command.OperationId,
                command.Provider,
                command.ModelId);
        }
    }

    private async Task NotifyFailureAsync(ModelDownloadCommand command, string errorSummary)
    {
        try
        {
            var notificationClient = _serviceProvider.GetService(typeof(MatrixNotificationClient)) as MatrixNotificationClient;
            if (notificationClient is null)
            {
                return;
            }

            var message =
                $"Model download failed ({command.Provider}/{command.ModelId}, role {command.RuntimeRole}, operation {command.OperationId}).\n" +
                $"Error: {errorSummary}";

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await notificationClient.SendTextMessageAsync(message, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failure notification for model download operation {OperationId} could not be sent.", command.OperationId);
        }
    }

    private static bool ModelIdMatches(string installedModelId, string requestedModelId)
    {
        if (string.Equals(installedModelId, requestedModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ollama normalizes untagged names to ":latest" in /api/tags.
        return string.Equals(installedModelId, $"{requestedModelId}:latest", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSummary(ModelDownloadCommand command, string outcome, string? digest, long? sizeBytes)
        => JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["provider"] = command.Provider,
                ["modelId"] = command.ModelId,
                ["runtimeRole"] = command.RuntimeRole,
                ["outcome"] = outcome,
                ["digest"] = digest,
                ["sizeBytes"] = sizeBytes
            },
            SummaryJsonOptions);
}
