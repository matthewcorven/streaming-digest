namespace StreamingDigest.Application;

/// <summary>
/// Shared fallback defaults for the model each runtime role uses when no configuration
/// key or environment variable supplies one. Referenced by the seam registrations
/// (<see cref="MeaiServiceCollectionExtensions"/>, <see cref="OllamaEmbeddingService"/>)
/// and by <see cref="ModelReadinessGuard"/> so the guard checks readiness of the same
/// model a seam would actually call — never a divergent literal (review Fix 2, issue #201).
/// </summary>
public static class ModelResolutionDefaults
{
    /// <summary>Fallback embedding model when nothing is configured.</summary>
    public const string EmbeddingModel = "nomic-embed-text";

    /// <summary>Fallback LLM model when nothing is configured.</summary>
    public const string LlmModel = "llama2";
}
