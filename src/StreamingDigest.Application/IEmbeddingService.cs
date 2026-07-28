using System.Collections.Generic;

namespace StreamingDigest.Application;

public interface IEmbeddingService
{
    Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public sealed record EmbeddingGenerationResult(string Model, int Dimensions, IReadOnlyList<double> Values);
