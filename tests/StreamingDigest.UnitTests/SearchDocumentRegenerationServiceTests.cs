using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.UnitTests;

public sealed class SearchDocumentRegenerationServiceTests
{
    [Fact]
    public async Task RegenerateAllAsync_reprocesses_external_resources_and_repositories_with_null_parent_video()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StreamingDigestDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new StreamingDigestDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var externalResource = new ExternalResource
        {
            Id = Guid.NewGuid(),
            CanonicalUrl = "https://example.com/article",
            Domain = "example.com",
            TitleOriginal = "Example article"
        };

        var repository = new RepositoryRecord
        {
            Id = Guid.NewGuid(),
            Host = "github.com",
            CanonicalUrl = "https://github.com/octo/repo",
            Name = "repo",
            DescriptionOriginal = "Repository description"
        };

        context.ExternalResources.Add(externalResource);
        context.Repositories.Add(repository);
        await context.SaveChangesAsync();

        var generator = new RecordingSearchDocumentGenerator();
        var store = new RecordingSearchDocumentEmbeddingStore();
        var service = new SearchDocumentRegenerationService(context, generator, store);

        await service.RegenerateAllAsync();

        var storedDocuments = store.StoredDocuments;
        var externalDocument = Assert.Single(storedDocuments, document => document.SourceEntityType == SearchDocumentSourceEntityTypes.ExternalResource);
        var repositoryDocument = Assert.Single(storedDocuments, document => document.SourceEntityType == SearchDocumentSourceEntityTypes.Repository);

        Assert.Null(externalDocument.ParentVideoId);
        Assert.Null(repositoryDocument.ParentVideoId);
        Assert.Equal(externalResource.Id, externalDocument.SourceEntityId);
        Assert.Equal(repository.Id, repositoryDocument.SourceEntityId);
    }

    private sealed class RecordingSearchDocumentGenerator : ISearchDocumentGenerator
    {
        public IReadOnlyList<GeneratedSearchDocument> Generate(SearchDocumentGenerationRequest request)
        {
            var documents = new List<GeneratedSearchDocument>();

            foreach (var resource in request.ExternalLinkMetadata)
            {
                documents.Add(new GeneratedSearchDocument(
                    SearchDocumentTypeNames.ExternalResourceMetadata,
                    SearchDocumentSourceEntityTypes.ExternalResource,
                    resource.SourceEntityId,
                    request.ParentVideoId,
                    resource.TitleOriginal ?? string.Empty,
                    resource.DescriptionOriginal ?? string.Empty,
                    "external-resource-content"));
            }

            foreach (var repo in request.RepositoryReadmeChunks)
            {
                documents.Add(new GeneratedSearchDocument(
                    SearchDocumentTypeNames.RepositoryReadmeChunk,
                    SearchDocumentSourceEntityTypes.Repository,
                    repo.SourceEntityId,
                    request.ParentVideoId,
                    string.Empty,
                    repo.ContentOriginal ?? string.Empty,
                    "repository-content"));
            }

            return documents;
        }
    }

    private sealed class RecordingSearchDocumentEmbeddingStore : ISearchDocumentEmbeddingStore
    {
        public List<GeneratedSearchDocument> StoredDocuments { get; } = [];

        public Task<IReadOnlyList<StoredSearchDocumentEmbedding>> StoreAsync(
            IEnumerable<GeneratedSearchDocument> documents,
            Guid? generatedByOperationId = null,
            CancellationToken cancellationToken = default)
        {
            var list = documents.ToList();
            StoredDocuments.AddRange(list);
            return Task.FromResult<IReadOnlyList<StoredSearchDocumentEmbedding>>(
                list.Select(document => new StoredSearchDocumentEmbedding(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "test-provider",
                    "test-model",
                    3,
                    document.ContentHash,
                    "source-text-hash")).ToList());
        }

        public Task DeleteForVideoScopeAsync(Guid videoId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteForSourceAsync(string sourceEntityType, Guid sourceEntityId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
