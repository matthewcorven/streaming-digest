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
}
