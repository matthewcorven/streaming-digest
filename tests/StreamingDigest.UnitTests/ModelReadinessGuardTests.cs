using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class ModelReadinessGuardTests
{
    private static IConfiguration BuildConfig(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Embedding_ready_when_state_status_ready()
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "Embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["embedding:model"] = "bge-m3" }));

        var readiness = await guard.CheckAsync(RuntimeRole.Embedding);

        Assert.True(readiness.IsReady);
        Assert.Equal("bge-m3", readiness.ModelId);
        Assert.Equal("ready", readiness.Status);
    }

    [Fact]
    public async Task Embedding_not_ready_when_no_state_row()
    {
        var repo = new FakeModelRuntimeStateRepository(null);
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["embedding:model"] = "bge-m3" }));

        var readiness = await guard.CheckAsync(RuntimeRole.Embedding);

        Assert.False(readiness.IsReady);
        Assert.Null(readiness.Status);
        Assert.Contains("not been acquired", readiness.Reason);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("downloading")]
    [InlineData("missing")]
    [InlineData("error")]
    public async Task Embedding_not_ready_for_non_ready_statuses(string status)
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "bge-m3",
            RuntimeRole = "Embedding",
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["embedding:model"] = "bge-m3" }));

        var readiness = await guard.CheckAsync(RuntimeRole.Embedding);

        Assert.False(readiness.IsReady);
        Assert.Equal(status, readiness.Status);
        Assert.Contains(status, readiness.Reason);
    }

    [Fact]
    public async Task Embedding_resolves_configured_model()
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "custom-embed",
            RuntimeRole = "Embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["embedding:model"] = "custom-embed" }));

        var readiness = await guard.CheckAsync(RuntimeRole.Embedding);

        Assert.True(readiness.IsReady);
        Assert.Equal("custom-embed", readiness.ModelId);
        Assert.Equal("custom-embed", repo.LastModelId);
    }

    [Fact]
    public async Task Llm_uses_llm_model_config_key()
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = "llama3.1:8b",
            RuntimeRole = "LLM",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["llm:model"] = "llama3.1:8b" }));

        var readiness = await guard.CheckAsync(RuntimeRole.LLM);

        Assert.True(readiness.IsReady);
        Assert.Equal("llama3.1:8b", readiness.ModelId);
        Assert.Equal("llama3.1:8b", repo.LastModelId);
    }

    [Fact]
    public async Task Whisper_unready_when_no_base_url_configured()
    {
        var repo = new FakeModelRuntimeStateRepository(null);
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?>()));

        var readiness = await guard.CheckAsync(RuntimeRole.Audio);

        Assert.False(readiness.IsReady);
        Assert.Equal("whisper", readiness.ModelId);
        Assert.Contains("not configured", readiness.Reason);
        // Whisper readiness is config-derived; the repository is never consulted.
        Assert.Null(repo.LastModelId);
    }

    [Fact]
    public async Task Whisper_ready_when_base_url_configured()
    {
        var repo = new FakeModelRuntimeStateRepository(null);
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?> { ["whisper:baseUrl"] = "http://localhost:9000" }));

        var readiness = await guard.CheckAsync(RuntimeRole.Audio);

        Assert.True(readiness.IsReady);
        Assert.Equal("whisper", readiness.ModelId);
    }

    // WS-7 review Fix 2: the guard must resolve the same fallback model a seam would actually
    // call when nothing is configured. Both sides reference ModelResolutionDefaults so the two
    // cannot silently diverge (guard checking bge-m3 while the seam calls nomic-embed-text).
    [Fact]
    public async Task Embedding_default_matches_seam_default()
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = ModelResolutionDefaults.EmbeddingModel,
            RuntimeRole = "Embedding",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?>()));

        var readiness = await guard.CheckAsync(RuntimeRole.Embedding);

        Assert.True(readiness.IsReady);
        Assert.Equal(ModelResolutionDefaults.EmbeddingModel, readiness.ModelId);
        Assert.Equal(ModelResolutionDefaults.EmbeddingModel, repo.LastModelId);
    }

    [Fact]
    public async Task Llm_default_matches_seam_default()
    {
        var repo = new FakeModelRuntimeStateRepository(new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = "ollama",
            ModelId = ModelResolutionDefaults.LlmModel,
            RuntimeRole = "LLM",
            Status = "ready",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var guard = new ModelReadinessGuard(repo, BuildConfig(new Dictionary<string, string?>()));

        var readiness = await guard.CheckAsync(RuntimeRole.LLM);

        Assert.True(readiness.IsReady);
        Assert.Equal(ModelResolutionDefaults.LlmModel, readiness.ModelId);
        Assert.Equal(ModelResolutionDefaults.LlmModel, repo.LastModelId);
    }

    private sealed class FakeModelRuntimeStateRepository : IModelRuntimeStateRepository
    {
        private readonly ModelRuntimeState? _state;

        public FakeModelRuntimeStateRepository(ModelRuntimeState? state) => _state = state;

        public string? LastModelId { get; private set; }

        public Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default)
        {
            LastModelId = modelId;
            return Task.FromResult(_state is not null && _state.ModelId == modelId ? _state : null);
        }

        public Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>([]);

        public Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelRuntimeState>>([]);
    }
}

public sealed class ModelReadinessNotifierTests
{
    private static ModelReadiness Unready(string modelId = "bge-m3") => new(
        RuntimeRole.Embedding,
        "ollama",
        modelId,
        IsReady: false,
        Status: "missing",
        Reason: "Embedding model is not ready.");

    [Fact]
    public void ReportDegraded_returns_false_when_ready()
    {
        var notifier = new ModelReadinessNotifier(new TestLogger<ModelReadinessNotifier>());
        var ready = new ModelReadiness(RuntimeRole.Embedding, "ollama", "bge-m3", true, "ready", "ready");

        var emitted = notifier.ReportDegraded("S1", ready, "action");

        Assert.False(emitted);
    }

    [Fact]
    public void ReportDegraded_emits_once_per_seam_and_model()
    {
        var notifier = new ModelReadinessNotifier(new TestLogger<ModelReadinessNotifier>());

        Assert.True(notifier.ReportDegraded("S1", Unready(), "action"));
        Assert.False(notifier.ReportDegraded("S1", Unready(), "action"));
        // Different seam -> emits again.
        Assert.True(notifier.ReportDegraded("S2", Unready(), "action"));
        // Different model -> emits again.
        Assert.True(notifier.ReportDegraded("S1", Unready("other-model"), "action"));
    }

    [Fact]
    public async Task ReportDegraded_writes_catalogued_domain_event_when_context_supplied()
    {
        var notifier = new ModelReadinessNotifier(new TestLogger<ModelReadinessNotifier>());
        await using var context = CreateDbContext();

        var emitted = notifier.ReportDegraded("S1", Unready(), "action", dbContext: context, entityType: "video", entityId: Guid.NewGuid());
        await context.SaveChangesAsync();

        Assert.True(emitted);
        var evt = Assert.Single(context.DomainEvents);
        Assert.Equal(DomainEventTypeCatalog.ModelReadinessDegraded, evt.EventType);
        Assert.True(DomainEventTypeCatalog.IsDefined(evt.EventType));
        Assert.Equal("warning", evt.Severity);
        Assert.Contains("S1", evt.Message);
    }

    [Fact]
    public void ReportDegraded_without_context_still_logs_and_reports()
    {
        var logger = new TestLogger<ModelReadinessNotifier>();
        var notifier = new ModelReadinessNotifier(logger);

        var emitted = notifier.ReportDegraded("S2", Unready(), "action");

        Assert.True(emitted);
        Assert.Contains(logger.Messages, message => message.Contains("S2"));
    }

    private static StreamingDigestDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new StreamingDigestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
