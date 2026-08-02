using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StreamingDigest.Application;

/// <summary>
/// Adapter that implements IEmbeddingService but delegates to Microsoft.Extensions.AI.IEmbeddingGenerator.
/// Preserves dimension guards and pgvector Vector conversion logic from the original OllamaEmbeddingService.
/// </summary>
public sealed class MeaiEmbeddingServiceAdapter : IEmbeddingService
{
    internal const string ProviderName = "ollama";
    private readonly IEmbeddingGenerator<string, Embedding<float>> _meaiGenerator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MeaiEmbeddingServiceAdapter>? _logger;

    public MeaiEmbeddingServiceAdapter(
        IEmbeddingGenerator<string, Embedding<float>> meaiGenerator,
        IConfiguration configuration,
        ILogger<MeaiEmbeddingServiceAdapter>? logger = null)
    {
        _meaiGenerator = meaiGenerator ?? throw new ArgumentNullException(nameof(meaiGenerator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
    }

    public async Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var expectedDimensions = ResolveExpectedDimensions();

        try
        {
            var embeddings = await _meaiGenerator.GenerateAsync(new[] { text }, new EmbeddingGenerationOptions(), cancellationToken);
            var embeddingList = embeddings.ToList();
            
            if (embeddingList.Count == 0 || embeddingList[0].Vector.Length == 0)
            {
                throw new InvalidOperationException("MEAI embedding generator returned an empty embedding response.");
            }

            var embedding = embeddingList[0];
            
            // Convert ReadOnlyMemory<float> to double[] for backward compatibility
            var vector = embedding.Vector.Span;
            var values = new List<double>(vector.Length);
            foreach (var f in vector)
            {
                values.Add((double)f);
            }

            if (expectedDimensions is > 0 && values.Count != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"MEAI embedding generator returned {values.Count} dimensions, but expected {expectedDimensions}.");
            }

            _logger?.LogDebug("Generated embedding with {DimensionCount} dimensions", values.Count);

            return new EmbeddingGenerationResult(ProviderName, "ollama", values.Count, values);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to generate embedding via MEAI");
            throw;
        }
    }

    private int? ResolveExpectedDimensions()
    {
        var configured = ResolveConfigurationValue(
            ["embedding:expectedDimensions", "embedding:dimensions", "embeddings:expectedDimensions", "embeddings:dimensions"],
            ["STREAMINGDIGEST_EMBEDDING_EXPECTED_DIMENSIONS", "STREAMINGDIGEST_EMBEDDING_DIMENSIONS"]);

        return int.TryParse(configured, out var expectedDimensions) ? expectedDimensions : null;
    }

    private string? ResolveConfigurationValue(IReadOnlyList<string> configurationKeys, IReadOnlyList<string> environmentVariables)
    {
        foreach (var key in configurationKeys)
        {
            var configuredValue = _configuration[key];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue.Trim();
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
