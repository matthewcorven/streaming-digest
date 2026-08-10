using StreamingDigest.Domain;

namespace StreamingDigest.Application.Models;

/// <summary>
/// Shared, read-only catalog of the models Streaming Digest supports. This is the single
/// source of truth used by both the API surface (model options, verify, download queueing)
/// and startup/runtime reconciliation against the model runtime's <c>/api/tags</c>.
/// Only <see cref="ModelProvider.Ollama"/> entries are downloadable; external providers
/// (OpenAI, Whisper) are verify-only per the model-download plan (WS-0).
/// </summary>
public static class ModelCatalog
{
    public static readonly IReadOnlyList<ModelOptionDefinition> SupportedModels =
    [
        new("bge-m3", "embedding", "available", "BAAI bge-m3", ModelProvider.Ollama, RuntimeRole.Embedding, true, "ollama pull bge-m3", "/mnt/models/embedding"),
        new("text-embedding-3-small", "embedding", "available", "OpenAI text-embedding-3-small", ModelProvider.OpenAI, RuntimeRole.Embedding, false, null, null),
        new("llama3.1:8b", "llm", "available", "Llama 3.1 8B", ModelProvider.Ollama, RuntimeRole.LLM, true, "ollama pull llama3.1:8b", "/mnt/models/llm"),
        new("qwen2.5:7b", "llm", "available", "Qwen 2.5 7B", ModelProvider.Ollama, RuntimeRole.LLM, true, "ollama pull qwen2.5:7b", "/mnt/models/llm"),
        new("whisper", "audio", "available", "Whisper Base", ModelProvider.Whisper, RuntimeRole.Audio, false, null, null)
    ];

    /// <summary>
    /// Lowercase provider name persisted in <c>model_runtime_state.provider</c> and surfaced
    /// by API responses (e.g. <c>"ollama"</c>, <c>"openai"</c>, <c>"whisper"</c>).
    /// </summary>
    public static string ToProviderName(ModelProvider provider) => provider.ToString().ToLowerInvariant();

    /// <summary>
    /// Lowercase runtime-role string persisted in <c>model_runtime_state.runtime_role</c>
    /// (e.g. <c>"embedding"</c>, <c>"llm"</c>, <c>"audio"</c>).
    /// </summary>
    public static string ToRuntimeRoleName(RuntimeRole role) => role switch
    {
        RuntimeRole.Embedding => "embedding",
        RuntimeRole.LLM => "llm",
        RuntimeRole.Audio => "audio",
        _ => role.ToString().ToLowerInvariant()
    };

    /// <summary>Finds a catalog entry by exact (case-insensitive) model id, or null.</summary>
    public static ModelOptionDefinition? FindById(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var normalized = modelId.Trim();
        return SupportedModels.FirstOrDefault(m => string.Equals(m.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a catalog entry by family (e.g. <c>"embedding"</c>, <c>"llm"</c>, <c>"audio"</c>).</summary>
    public static ModelOptionDefinition? FindByFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var normalized = family.Trim();
        return SupportedModels.FirstOrDefault(m => string.Equals(m.Family, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
