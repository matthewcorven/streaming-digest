using Microsoft.Extensions.Configuration;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// Default <see cref="IModelReadinessGuard"/>. Resolves the currently-configured model for a
/// role from the same configuration keys / environment variables the runtime seams already use,
/// then reads its status from <c>model_runtime_state</c> via <see cref="IModelRuntimeStateRepository"/>.
/// Whisper is an external verify-only service (plan §8 S6): it has no ollama presence row, so its
/// readiness is derived from whether the service endpoint is configured.
/// </summary>
public sealed class ModelReadinessGuard : IModelReadinessGuard
{
    private readonly IModelRuntimeStateRepository _repository;
    private readonly IConfiguration? _configuration;

    public ModelReadinessGuard(IModelRuntimeStateRepository repository, IConfiguration? configuration = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _configuration = configuration;
    }

    public async Task<ModelReadiness> CheckAsync(RuntimeRole role, CancellationToken cancellationToken = default)
    {
        if (role == RuntimeRole.Audio)
        {
            return CheckWhisper();
        }

        var (provider, modelId) = ResolveModel(role);
        var state = await _repository.GetByProviderAndModelIdAsync(provider, modelId, cancellationToken);

        if (state is null)
        {
            return new ModelReadiness(
                role,
                provider,
                modelId,
                IsReady: false,
                Status: null,
                Reason: $"No {role} model '{modelId}' is recorded in model_runtime_state for provider '{provider}'. The model has not been acquired/verified yet.");
        }

        var isReady = string.Equals(state.Status, ModelRuntimeStatuses.Ready, StringComparison.OrdinalIgnoreCase);
        var reason = isReady
            ? $"{role} model '{modelId}' is ready."
            : $"{role} model '{modelId}' is not ready (status '{state.Status}').";

        return new ModelReadiness(role, provider, modelId, isReady, state.Status, reason);
    }

    private ModelReadiness CheckWhisper()
    {
        var baseUrl = ResolveConfigurationValue(
            ["whisper:baseUrl", "audioToText:baseUrl", "audioToText:whisper:baseUrl"],
            ["STREAMINGDIGEST_WHISPER_BASE_URL", "WHISPER_BASE_URL"]);

        var configured = !string.IsNullOrWhiteSpace(baseUrl);
        return new ModelReadiness(
            RuntimeRole.Audio,
            Provider: ModelProvider.Whisper.ToString().ToLowerInvariant(),
            ModelId: "whisper",
            IsReady: configured,
            Status: configured ? ModelRuntimeStatuses.Ready : ModelRuntimeStatuses.Missing,
            Reason: configured
                ? "Whisper audio-to-text service is configured."
                : "Whisper audio-to-text service is not configured (no whisper base URL).");
    }

    private (string Provider, string ModelId) ResolveModel(RuntimeRole role)
    {
        return role switch
        {
            RuntimeRole.Embedding => (
                ModelProvider.Ollama.ToString().ToLowerInvariant(),
                ResolveConfigurationValue(
                    ["embedding:model", "embeddings:model"],
                    ["STREAMINGDIGEST_EMBEDDING_MODEL"]) ?? ModelResolutionDefaults.EmbeddingModel),
            RuntimeRole.LLM => (
                ModelProvider.Ollama.ToString().ToLowerInvariant(),
                ResolveConfigurationValue(
                    ["llm:model"],
                    ["STREAMINGDIGEST_LLM_MODEL", "OLLAMA_MODEL"]) ?? ModelResolutionDefaults.LlmModel),
            _ => (
                ModelProvider.Ollama.ToString().ToLowerInvariant(),
                "unknown")
        };
    }

    private string? ResolveConfigurationValue(IReadOnlyList<string> configurationKeys, IReadOnlyList<string> environmentVariables)
    {
        if (_configuration is not null)
        {
            foreach (var key in configurationKeys)
            {
                var configuredValue = _configuration[key];
                if (!string.IsNullOrWhiteSpace(configuredValue))
                {
                    return configuredValue.Trim();
                }
            }
        }

        foreach (var variable in environmentVariables)
        {
            var environmentValue = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }
        }

        return null;
    }
}
