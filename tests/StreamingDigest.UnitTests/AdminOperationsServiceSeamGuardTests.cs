using StreamingDigest.Application;
using StreamingDigest.Application.Admin;
using StreamingDigest.Application.Models;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class AdminOperationsServiceSeamGuardTests
{
    [Fact]
    public async Task TestEmbeddingService_returns_not_ready_when_guard_unready()
    {
        var unready = new ModelReadiness(RuntimeRole.Embedding, "ollama", "bge-m3", false, "missing", "Embedding model is not ready.");
        var service = new AdminOperationsService(modelReadinessGuard: new FakeModelReadinessGuard(unready));

        var result = await service.TestEmbeddingServiceAsync();

        Assert.Equal("failed", result.Status);
        Assert.Contains("not ready", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Embedding model is not ready.", result.Message);
    }

    [Fact]
    public async Task TestEmbeddingService_skips_embedding_call_when_guard_unready()
    {
        var unready = new ModelReadiness(RuntimeRole.Embedding, "ollama", "bge-m3", false, "missing", "Embedding model is not ready.");
        var embeddingService = new RecordingEmbeddingService();
        var service = new AdminOperationsService(embeddingService: embeddingService, modelReadinessGuard: new FakeModelReadinessGuard(unready));

        var result = await service.TestEmbeddingServiceAsync();

        Assert.Equal("failed", result.Status);
        Assert.Equal(0, embeddingService.CallCount);
    }

    [Fact]
    public async Task TestEmbeddingService_invokes_embedding_when_guard_ready()
    {
        var ready = new ModelReadiness(RuntimeRole.Embedding, "ollama", "bge-m3", true, "ready", "ready");
        var embeddingService = new RecordingEmbeddingService();
        var service = new AdminOperationsService(embeddingService: embeddingService, modelReadinessGuard: new FakeModelReadinessGuard(ready));

        var result = await service.TestEmbeddingServiceAsync();

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, embeddingService.CallCount);
    }

    private sealed class FakeModelReadinessGuard : IModelReadinessGuard
    {
        private readonly ModelReadiness _readiness;

        public FakeModelReadinessGuard(ModelReadiness readiness) => _readiness = readiness;

        public Task<ModelReadiness> CheckAsync(RuntimeRole role, CancellationToken cancellationToken = default)
            => Task.FromResult(_readiness);
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<EmbeddingGenerationResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EmbeddingGenerationResult("ollama", "bge-m3", 2, [0.1, 0.2]));
        }
    }
}
