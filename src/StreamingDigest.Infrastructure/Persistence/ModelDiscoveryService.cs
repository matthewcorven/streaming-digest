using System.Text.Json;
using StreamingDigest.Application;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class ModelDiscoveryService
{
    private static readonly IReadOnlyList<ModelOptionDefinition> SupportedModels =
    [
        new("bge-m3", "embedding", "available", "BAAI bge-m3", ModelProvider.Ollama, RuntimeRole.Embedding, true, "ollama pull bge-m3", "/mnt/models/embedding"),
        new("text-embedding-3-small", "embedding", "available", "OpenAI text-embedding-3-small", ModelProvider.OpenAI, RuntimeRole.Embedding, false, null, null),
        new("llama3.1:8b", "llm", "available", "Llama 3.1 8B", ModelProvider.Ollama, RuntimeRole.LLM, true, "ollama pull llama3.1:8b", "/mnt/models/llm"),
        new("qwen2.5:7b", "llm", "available", "Qwen 2.5 7B", ModelProvider.Ollama, RuntimeRole.LLM, true, "ollama pull qwen2.5:7b", "/mnt/models/llm"),
        new("whisper", "audio", "available", "Whisper Base", ModelProvider.Whisper, RuntimeRole.Audio, false, null, null)
    ];

    private readonly AppReadinessStateService _readinessStateService;
    private readonly IModelRuntimeClient? _modelRuntimeClient;
    private readonly IModelRuntimeStateRepository? _runtimeStateRepository;
    private readonly IAudioToTextProvider? _audioToTextProvider;

    public ModelDiscoveryService(
        AppReadinessStateService readinessStateService,
        IModelRuntimeClient? modelRuntimeClient = null,
        IModelRuntimeStateRepository? runtimeStateRepository = null,
        IAudioToTextProvider? audioToTextProvider = null)
    {
        _readinessStateService = readinessStateService;
        _modelRuntimeClient = modelRuntimeClient;
        _runtimeStateRepository = runtimeStateRepository;
        _audioToTextProvider = audioToTextProvider;
    }

    public IReadOnlyList<ModelOptionDefinition> GetSupportedModels() => SupportedModels.ToList();

    public async Task<ModelDownloadResult> QueueDownloadAsync(string connectionString, string? modelKind, string? modelId, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelKind, modelId);
        var operationId = Guid.NewGuid();
        var statusUrl = $"/api/admin/operations/{operationId}";

        await RecordReadinessVerificationAsync(connectionString, model, cancellationToken, "queued");

        return new ModelDownloadResult("queued", model.Family, model.Id, operationId, statusUrl);
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
        return health.IsHealthy
            ? new ModelProbeResult(true, false, health.Reason)
            : new ModelProbeResult(false, false, health.Reason);
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
                        ["verified"] = probe.Verified,
                        ["requestedAt"] = DateTimeOffset.UtcNow
                    }),
                cancellationToken);
        }
        catch
        {
            // Best-effort onboarding state updates are not required for the model discovery flow to proceed.
        }
    }

    private static ModelOptionDefinition ResolveModel(string? modelKind, string? modelId)
    {
        var normalizedKind = Normalize(modelKind);
        var normalizedModelId = Normalize(modelId);

        if (!string.IsNullOrWhiteSpace(normalizedModelId))
        {
            var byId = SupportedModels.FirstOrDefault(model => string.Equals(model.Id, normalizedModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null && (normalizedKind is null || string.Equals(byId.Family, normalizedKind, StringComparison.OrdinalIgnoreCase)))
            {
                return byId;
            }
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

    private async Task RecordReadinessVerificationAsync(string connectionString, ModelOptionDefinition model, CancellationToken cancellationToken, string status)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var stepKey = ResolveReadinessStepKey(model);

        try
        {
            await _readinessStateService.VerifyStepAsync(
                connectionString,
                stepKey,
                new OnboardingStepVerificationRequest(
                    status,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["provider"] = model.Provider.ToString().ToLowerInvariant(),
                        ["modelId"] = model.Id,
                        ["modelKind"] = model.Family,
                        ["modelLabel"] = model.Label,
                        ["requestedAt"] = DateTimeOffset.UtcNow
                    }),
                cancellationToken);
        }
        catch
        {
            // Best-effort onboarding state updates are not required for the model discovery flow to proceed.
        }
    }

    private static string ResolveReadinessStepKey(ModelOptionDefinition model) => model.Family switch
    {
        "embedding" => "embedding_model_verified",
        "llm" => "llm_model_verified",
        "audio" => "audio_to_text_verified",
        _ => "embedding_model_verified"
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ModelProbeResult(bool Verified, bool PresentInRuntime, string Message);
}

public sealed record ModelOptionDefinition(
    string Id,
    string Family,
    string Status,
    string Label,
    ModelProvider Provider,
    RuntimeRole RuntimeRole,
    bool Downloadable,
    string? InstallCommand = null,
    string? MountPath = null);

public sealed record ModelDownloadResult(string Status, string ModelKind, string ModelId, Guid OperationId, string StatusUrl);

public sealed record ModelVerificationResult(string Status, string ModelKind, string ModelId, bool Verified, string Message);
