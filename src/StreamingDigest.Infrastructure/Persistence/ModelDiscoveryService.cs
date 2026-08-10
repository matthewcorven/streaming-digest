using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class ModelDiscoveryService
{
    private static readonly IReadOnlyList<ModelOptionDefinition> SupportedModels = ModelCatalog.SupportedModels;

    private readonly AppReadinessStateService _readinessStateService;
    private readonly IModelRuntimeClient? _modelRuntimeClient;
    private readonly IModelRuntimeStateRepository? _runtimeStateRepository;
    private readonly IAudioToTextProvider? _audioToTextProvider;
    private readonly ILogger<ModelDiscoveryService>? _logger;

    public ModelDiscoveryService(
        AppReadinessStateService readinessStateService,
        IModelRuntimeClient? modelRuntimeClient = null,
        IModelRuntimeStateRepository? runtimeStateRepository = null,
        IAudioToTextProvider? audioToTextProvider = null,
        ILogger<ModelDiscoveryService>? logger = null)
    {
        _readinessStateService = readinessStateService;
        _modelRuntimeClient = modelRuntimeClient;
        _runtimeStateRepository = runtimeStateRepository;
        _audioToTextProvider = audioToTextProvider;
        _logger = logger;
    }

    public IReadOnlyList<ModelOptionDefinition> GetSupportedModels() => SupportedModels.ToList();

    /// <summary>
    /// Resolves a catalog model for a download request, rejecting anything that is not a
    /// downloadable Ollama model. Kept side-effect free; durable persistence and enqueueing
    /// happen in the API endpoint before it returns 202 (WS-5).
    /// </summary>
    public ModelOptionDefinition ResolveDownloadableModel(string? modelKind, string? modelId)
    {
        var model = ResolveModel(modelKind, modelId);
        if (!model.Downloadable || model.Provider != ModelProvider.Ollama)
        {
            throw new ArgumentException($"The requested model '{model.Id}' is verify-only and cannot be downloaded.", nameof(modelId));
        }

        return model;
    }

    /// <summary>
    /// Runs a real presence/health probe for the requested model and projects onboarding
    /// readiness from the probe result (never an optimistic success). Ollama models are probed
    /// against the runtime tag list (<see cref="IModelRuntimeClient.ListInstalledModelsAsync"/>);
    /// whisper is probed via the audio-to-text service <c>/health</c> check. Verified presence
    /// also marks the runtime state <c>ready</c> and updates <c>last_seen_in_runtime_at</c>.
    /// </summary>
    public async Task<ModelVerificationResult> VerifyModelAsync(string connectionString, string? modelKind, string? modelId, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelKind, modelId);
        var probe = await ProbeModelPresenceAsync(model, cancellationToken);

        await PersistRuntimeStateAsync(model, probe, cancellationToken);
        await ProjectReadinessFromProbeAsync(connectionString, model, probe, cancellationToken);

        return new ModelVerificationResult(
            probe.Verified ? "verified" : "failed",
            model.Family,
            model.Id,
            probe.Verified,
            probe.Message);
    }

    private async Task<ModelProbeResult> ProbeModelPresenceAsync(ModelOptionDefinition model, CancellationToken cancellationToken)
    {
        return model.Provider switch
        {
            ModelProvider.Ollama => await ProbeOllamaPresenceAsync(model, cancellationToken),
            ModelProvider.Whisper => await ProbeWhisperHealthAsync(model, cancellationToken),
            _ => new ModelProbeResult(false, false,
                $"{model.Label} is managed externally by provider '{model.Provider.ToString().ToLowerInvariant()}'; there is no local runtime probe. Configure the provider and verify from that provider.")
        };
    }

    private async Task<ModelProbeResult> ProbeOllamaPresenceAsync(ModelOptionDefinition model, CancellationToken cancellationToken)
    {
        if (_modelRuntimeClient is null)
        {
            return new ModelProbeResult(false, false, "The Ollama runtime client is not configured; model presence cannot be probed.");
        }

        IReadOnlyList<ModelPresence> installed;
        try
        {
            installed = await _modelRuntimeClient.ListInstalledModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ModelProbeResult(false, false, $"The Ollama runtime probe failed: {ex.Message}");
        }

        var match = installed.FirstOrDefault(presence => ModelIdsMatch(presence.ModelId, model.Id));
        return match is null
            ? new ModelProbeResult(false, true, $"{model.Label} is not installed in the Ollama runtime. Download it before verifying.")
            : new ModelProbeResult(true, true, $"{model.Label} is installed and ready.");
    }

    private async Task<ModelProbeResult> ProbeWhisperHealthAsync(ModelOptionDefinition model, CancellationToken cancellationToken)
    {
        if (_audioToTextProvider is null)
        {
            return new ModelProbeResult(false, false, "The audio-to-text provider is not configured; whisper health cannot be probed.");
        }

        var health = await _audioToTextProvider.CheckHealthAsync(cancellationToken);
        // /health proves the whisper *service* is reachable, not that the configured model is
        // loaded — say so precisely rather than implying model readiness (unlike the Ollama tag
        // list, which does prove model presence).
        return health.IsHealthy
            ? new ModelProbeResult(true, false,
                $"Whisper service is reachable at {health.Endpoint} (health probe passed). Model load is verified on first transcription.",
                ProbeKind: "service_health")
            : new ModelProbeResult(false, false, health.Reason, ProbeKind: "service_health");
    }

    // Ollama reports installed models with an implicit ":latest" tag when the tag was not
    // requested explicitly, so "bge-m3" must match a runtime entry of "bge-m3:latest".
    private static bool ModelIdsMatch(string runtimeModelId, string catalogModelId)
    {
        if (string.Equals(runtimeModelId, catalogModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return runtimeModelId.StartsWith($"{catalogModelId}:", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PersistRuntimeStateAsync(ModelOptionDefinition model, ModelProbeResult probe, CancellationToken cancellationToken)
    {
        if (_runtimeStateRepository is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _runtimeStateRepository.GetByProviderAndModelIdAsync(
            model.Provider.ToString().ToLowerInvariant(), model.Id, cancellationToken);

        var state = new ModelRuntimeState
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Provider = model.Provider.ToString().ToLowerInvariant(),
            ModelId = model.Id,
            RuntimeRole = model.RuntimeRole.ToString().ToLowerInvariant(),
            Status = probe.Verified ? "ready" : "failed",
            CurrentOperationId = existing?.CurrentOperationId,
            ProgressPercent = probe.Verified ? 100 : existing?.ProgressPercent,
            LastVerifiedAt = probe.Verified ? now : existing?.LastVerifiedAt,
            LastSeenInRuntimeAt = probe.PresentInRuntime ? now : existing?.LastSeenInRuntimeAt,
            LastErrorSummary = probe.Verified ? null : probe.Message,
            DetailsJson = JsonSerializer.Serialize(new
            {
                provider = model.Provider.ToString().ToLowerInvariant(),
                modelId = model.Id,
                modelKind = model.Family,
                modelLabel = model.Label,
                probe = "verify",
                probeKind = probe.ProbeKind,
                verified = probe.Verified,
                message = probe.Message,
                probedAt = now
            }),
            UpdatedAt = now
        };

        await _runtimeStateRepository.UpsertAsync(state, cancellationToken);
    }

    private async Task ProjectReadinessFromProbeAsync(string connectionString, ModelOptionDefinition model, ModelProbeResult probe, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var stepKey = ResolveReadinessStepKey(model);
        var status = probe.Verified ? "succeeded" : "failed";

        try
        {
            await _readinessStateService.VerifyStepAsync(
                connectionString,
                stepKey,
                new OnboardingStepVerificationRequest(
                    status,
                    probe.Verified ? null : probe.Message,
                    new Dictionary<string, object?>
                    {
                        ["provider"] = model.Provider.ToString().ToLowerInvariant(),
                        ["modelId"] = model.Id,
                        ["modelKind"] = model.Family,
                        ["modelLabel"] = model.Label,
                        ["probe"] = "verify",
                        ["probeKind"] = probe.ProbeKind,
                        ["verified"] = probe.Verified,
                        ["requestedAt"] = DateTimeOffset.UtcNow
                    }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort onboarding state updates are not required for the model discovery flow to
            // proceed, but per the 2026-08-01 directive we signal rather than silently degrade:
            // model_runtime_state was persisted, app_readiness_checks was not.
            _logger?.LogWarning(
                ex,
                "Readiness projection for step {StepKey} failed after verify of model {ModelId}; model_runtime_state was persisted but app_readiness_checks was not updated.",
                stepKey,
                model.Id);
        }
    }

    private static ModelOptionDefinition ResolveModel(string? modelKind, string? modelId)
    {
        var normalizedKind = Normalize(modelKind);
        var normalizedModelId = Normalize(modelId);

        if (!string.IsNullOrWhiteSpace(normalizedModelId))
        {
            var byId = SupportedModels.FirstOrDefault(model => string.Equals(model.Id, normalizedModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                // Exact-ID wins over kind; a mismatched kind is a caller error, not a silent
                // substitution (reconciles #234's ID-first change with WS-5's strictness).
                if (!string.IsNullOrWhiteSpace(normalizedKind)
                    && !string.Equals(byId.Family, normalizedKind, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"The requested model '{normalizedModelId}' does not belong to the '{normalizedKind}' model kind.",
                        nameof(modelId));
                }

                return byId;
            }

            throw new ArgumentException($"The requested model '{normalizedModelId}' is not currently supported.", nameof(modelId));
        }

        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            var byKind = SupportedModels.FirstOrDefault(model => string.Equals(model.Family, normalizedKind, StringComparison.OrdinalIgnoreCase));
            if (byKind is not null)
            {
                return byKind;
            }
        }

        throw new ArgumentException($"The requested model '{modelId ?? "(null)"}' is not currently supported.", nameof(modelId));
    }

    private static string ResolveReadinessStepKey(ModelOptionDefinition model) => model.Family switch
    {
        "embedding" => "embedding_model_verified",
        "llm" => "llm_model_verified",
        "audio" => "audio_to_text_verified",
        _ => "embedding_model_verified"
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ModelProbeResult(bool Verified, bool PresentInRuntime, string Message, string? ProbeKind = null);
}

public sealed record ModelVerificationResult(string Status, string ModelKind, string ModelId, bool Verified, string Message);
