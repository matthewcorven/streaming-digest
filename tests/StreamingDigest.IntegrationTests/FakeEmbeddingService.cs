using StreamingDigest.Application;

namespace StreamingDigest.IntegrationTests;

internal sealed class FakeEmbeddingService : IEmbeddingService
{
    public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var seed = text.Length;
        IReadOnlyList<double> values =
        [
            seed,
            seed % 17,
            seed % 31
        ];

        return Task.FromResult(new EmbeddingGenerationResult("test-provider", "test-model", values.Count, values));
    }
}