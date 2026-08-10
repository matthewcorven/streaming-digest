using StreamingDigest.Application;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class ModelDiscoveryServiceTests
{
    [Fact]
    public async Task QueueDownloadAsync_UsesAdminOperationStatusUrl()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var result = await service.QueueDownloadAsync(string.Empty, "embedding", "bge-m3");

        Assert.Equal("queued", result.Status);
        Assert.StartsWith("/api/admin/operations/", result.StatusUrl, StringComparison.Ordinal);
        Assert.EndsWith(result.OperationId.ToString(), result.StatusUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSupportedModels_ReturnsAllModels()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();

        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Id == "bge-m3");
        Assert.Contains(models, m => m.Id == "text-embedding-3-small");
        Assert.Contains(models, m => m.Id == "llama3.1:8b");
        Assert.Contains(models, m => m.Id == "qwen2.5:7b");
        Assert.Contains(models, m => m.Id == "whisper");
    }

    [Fact]
    public void GetSupportedModels_OnlyOllamaProvidersAreDownloadable()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var ollamaModels = models.Where(m => m.Provider == ModelProvider.Ollama);
        var nonOllamaModels = models.Where(m => m.Provider != ModelProvider.Ollama);

        Assert.All(ollamaModels, m => Assert.True(m.Downloadable, $"Model {m.Id} with Ollama provider should be downloadable"));
        Assert.All(nonOllamaModels, m => Assert.False(m.Downloadable, $"Model {m.Id} with non-Ollama provider should not be downloadable"));
    }

    [Fact]
    public void GetSupportedModels_TextEmbedding3SmallIsOpenAI()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var textEmbedding3Small = models.First(m => m.Id == "text-embedding-3-small");

        Assert.Equal(ModelProvider.OpenAI, textEmbedding3Small.Provider);
        Assert.False(textEmbedding3Small.Downloadable);
        Assert.Null(textEmbedding3Small.InstallCommand);
        Assert.Null(textEmbedding3Small.MountPath);
    }

    [Fact]
    public void GetSupportedModels_WhisperIsWhisperProvider()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var whisper = models.First(m => m.Id == "whisper");

        Assert.Equal(ModelProvider.Whisper, whisper.Provider);
        Assert.False(whisper.Downloadable);
        Assert.Null(whisper.InstallCommand);
        Assert.Null(whisper.MountPath);
    }

    [Theory]
    [InlineData("bge-m3", RuntimeRole.Embedding)]
    [InlineData("text-embedding-3-small", RuntimeRole.Embedding)]
    [InlineData("llama3.1:8b", RuntimeRole.LLM)]
    [InlineData("qwen2.5:7b", RuntimeRole.LLM)]
    [InlineData("whisper", RuntimeRole.Audio)]
    public void GetSupportedModels_ModelHasCorrectRuntimeRole(string modelId, RuntimeRole expectedRole)
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var model = models.First(m => m.Id == modelId);

        Assert.Equal(expectedRole, model.RuntimeRole);
    }

    [Fact]
    public void GetSupportedModels_BgeMmbedderIsOllama()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var bge = models.First(m => m.Id == "bge-m3");

        Assert.Equal(ModelProvider.Ollama, bge.Provider);
        Assert.True(bge.Downloadable);
    }

    [Fact]
    public void GetSupportedModels_LlamaModelsAreOllama()
    {
        var service = new ModelDiscoveryService(new AppReadinessStateService());

        var models = service.GetSupportedModels();
        var llama = models.First(m => m.Id == "llama3.1:8b");
        var qwen = models.First(m => m.Id == "qwen2.5:7b");

        Assert.Equal(ModelProvider.Ollama, llama.Provider);
        Assert.True(llama.Downloadable);
        Assert.Equal(ModelProvider.Ollama, qwen.Provider);
        Assert.True(qwen.Downloadable);
    }

    [Fact]
    public async Task VerifyModelAsync_OllamaModelPresent_ReturnsVerifiedAndPersistsReadyState()
    {
        var runtimeClient = new StubModelRuntimeClient(new ModelPresence("ollama", "bge-m3", "sha256:abc", 1234));
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, repository);

        var result = await service.VerifyModelAsync(string.Empty, "embedding", "bge-m3");

        Assert.True(result.Verified);
        Assert.Equal("verified", result.Status);
        var state = Assert.Single(repository.States);
        Assert.Equal("ollama", state.Provider);
        Assert.Equal("bge-m3", state.ModelId);
        Assert.Equal("embedding", state.RuntimeRole);
        Assert.Equal("ready", state.Status);
        Assert.NotNull(state.LastVerifiedAt);
        Assert.NotNull(state.LastSeenInRuntimeAt);
        Assert.Null(state.LastErrorSummary);
    }

    [Fact]
    public async Task VerifyModelAsync_OllamaModelPresentWithImplicitLatestTag_ReturnsVerified()
    {
        var runtimeClient = new StubModelRuntimeClient(new ModelPresence("ollama", "llama3.1:latest", null, null));
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, repository);

        var result = await service.VerifyModelAsync(string.Empty, "llm", "llama3.1:8b");

        // The runtime reports "llama3.1:latest" which must not satisfy a "llama3.1:8b" request.
        Assert.False(result.Verified);
        Assert.Equal("failed", result.Status);
    }

    [Fact]
    public async Task VerifyModelAsync_OllamaModelMissing_ProjectsFailure()
    {
        var runtimeClient = new StubModelRuntimeClient();
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, repository);

        var result = await service.VerifyModelAsync(string.Empty, "llm", "llama3.1:8b");

        Assert.False(result.Verified);
        Assert.Equal("failed", result.Status);
        Assert.Contains("not installed", result.Message, StringComparison.OrdinalIgnoreCase);
        var state = Assert.Single(repository.States);
        Assert.Equal("failed", state.Status);
        Assert.Null(state.LastVerifiedAt);
        Assert.NotNull(state.LastErrorSummary);
    }

    [Fact]
    public async Task VerifyModelAsync_OllamaRuntimeUnreachable_ProjectsFailure()
    {
        var runtimeClient = new StubModelRuntimeClient(new InvalidOperationException("connection refused"));
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, repository);

        var result = await service.VerifyModelAsync(string.Empty, "embedding", "bge-m3");

        Assert.False(result.Verified);
        Assert.Contains("probe failed", result.Message, StringComparison.OrdinalIgnoreCase);
        var state = Assert.Single(repository.States);
        Assert.Equal("failed", state.Status);
        Assert.Null(state.LastSeenInRuntimeAt);
    }

    [Fact]
    public async Task VerifyModelAsync_WhisperHealthy_ReturnsVerifiedWithoutLastSeen()
    {
        var audioProvider = new StubAudioToTextProvider(new AudioToTextHealthResult(true, "whisper", "http://whisper:8080", "Whisper service health check succeeded (HTTP 200)."));
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), modelRuntimeClient: null, repository, audioProvider);

        var result = await service.VerifyModelAsync(string.Empty, "audio", "whisper");

        Assert.True(result.Verified);
        var state = Assert.Single(repository.States);
        Assert.Equal("whisper", state.Provider);
        Assert.Equal("audio", state.RuntimeRole);
        Assert.Equal("ready", state.Status);
        Assert.NotNull(state.LastVerifiedAt);
        // Health probes do not observe a runtime tag list, so last_seen_in_runtime_at stays null.
        Assert.Null(state.LastSeenInRuntimeAt);
    }

    [Fact]
    public async Task VerifyModelAsync_WhisperUnhealthy_ProjectsFailure()
    {
        var audioProvider = new StubAudioToTextProvider(new AudioToTextHealthResult(false, "whisper-unconfigured", null, "Audio-to-text is not configured."));
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), modelRuntimeClient: null, repository, audioProvider);

        var result = await service.VerifyModelAsync(string.Empty, "audio", "whisper");

        Assert.False(result.Verified);
        Assert.Contains("not configured", result.Message, StringComparison.OrdinalIgnoreCase);
        var state = Assert.Single(repository.States);
        Assert.Equal("failed", state.Status);
        Assert.Null(state.LastVerifiedAt);
    }

    [Fact]
    public async Task VerifyModelAsync_OpenAiModel_ReportsNoLocalProbe()
    {
        var repository = new InMemoryModelRuntimeStateRepository();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), modelRuntimeClient: null, repository);

        var result = await service.VerifyModelAsync(string.Empty, "embedding", "text-embedding-3-small");

        Assert.False(result.Verified);
        Assert.Contains("managed externally", result.Message, StringComparison.OrdinalIgnoreCase);
        var state = Assert.Single(repository.States);
        Assert.Equal("openai", state.Provider);
        Assert.Equal("failed", state.Status);
    }

    [Fact]
    public async Task VerifyModelAsync_FailedProbePreservesPriorVerifiedTimestamp()
    {
        var priorVerifiedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var repository = new InMemoryModelRuntimeStateRepository
        {
            Seed = new ModelRuntimeState
            {
                Id = Guid.NewGuid(),
                Provider = "ollama",
                ModelId = "bge-m3",
                RuntimeRole = "embedding",
                Status = "ready",
                LastVerifiedAt = priorVerifiedAt,
                UpdatedAt = priorVerifiedAt
            }
        };
        var runtimeClient = new StubModelRuntimeClient();
        var service = new ModelDiscoveryService(new AppReadinessStateService(), runtimeClient, repository);

        var result = await service.VerifyModelAsync(string.Empty, "embedding", "bge-m3");

        Assert.False(result.Verified);
        var state = Assert.Single(repository.States);
        Assert.Equal("failed", state.Status);
        Assert.Equal(priorVerifiedAt, state.LastVerifiedAt);
    }

    private sealed class StubModelRuntimeClient : IModelRuntimeClient
    {
        private readonly IReadOnlyList<ModelPresence> _installed;
        private readonly Exception? _failure;

        public StubModelRuntimeClient(params ModelPresence[] installed) => _installed = installed;

        public StubModelRuntimeClient(Exception failure) : this() => _failure = failure;

        public Task<IReadOnlyList<ModelPresence>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
            => _failure is not null ? Task.FromException<IReadOnlyList<ModelPresence>>(_failure) : Task.FromResult(_installed);

        public IAsyncEnumerable<ModelPullProgress> PullModelAsync(string model, bool stream = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ModelRuntimeInfo> ShowModelAsync(string model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubAudioToTextProvider(AudioToTextHealthResult health) : IAudioToTextProvider
    {
        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<AudioToTextHealthResult> CheckHealthAsync(CancellationToken ct) => Task.FromResult(health);
    }

    private sealed class InMemoryModelRuntimeStateRepository : IModelRuntimeStateRepository
    {
        private readonly List<ModelRuntimeState> _states = [];

        public ModelRuntimeState? Seed { get; init; }

        public IReadOnlyList<ModelRuntimeState> States => _states;

        public Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
        {
            _states.RemoveAll(existing => existing.Provider == state.Provider && existing.ModelId == state.ModelId);
            _states.Add(state);
            return Task.CompletedTask;
        }

        public Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
        {
            var match = _states.FirstOrDefault(s => s.Provider == provider && s.ModelId == modelId)
                ?? (Seed is not null && Seed.Provider == provider && Seed.ModelId == modelId ? Seed : null);
            return Task.FromResult(match);
        }

        public Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(_states.Where(s => s.Provider == provider).ToList());

        public Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>(_states.ToList());
    }
}
