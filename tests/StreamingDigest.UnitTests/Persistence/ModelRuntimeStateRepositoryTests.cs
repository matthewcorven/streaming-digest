using System.Security.Cryptography;
using System.Text;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class ModelRuntimeStateRepositoryTests
{
    private string _connectionString = null!;
    private IModelRuntimeStateRepository _repository = null!;

    public ModelRuntimeStateRepositoryTests()
    {
        // Use a test database connection string (not required to exist for validation tests)
        _connectionString = "Server=localhost;Port=5432;Database=streamingdigest_test;User Id=streamingdigest;Password=password";
        _repository = new PostgresModelRuntimeStateRepository(_connectionString);
    }

    [Fact]
    public async Task GetByProviderAndModelIdAsync_WithNullProvider_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.GetByProviderAndModelIdAsync(null!, "model-id"));
    }

    [Fact]
    public async Task GetByProviderAndModelIdAsync_WithNullModelId_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.GetByProviderAndModelIdAsync("ollama", null!));
    }

    [Fact]
    public async Task GetByProviderAndModelIdAsync_WithEmptyProvider_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () => await _repository.GetByProviderAndModelIdAsync("", "model-id"));
    }

    [Fact]
    public async Task GetByProviderAndModelIdAsync_WithEmptyModelId_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () => await _repository.GetByProviderAndModelIdAsync("ollama", ""));
    }

    [Fact]
    public async Task GetByProviderAsync_WithNullProvider_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.GetByProviderAsync(null!));
    }

    [Fact]
    public async Task GetByProviderAsync_WithEmptyProvider_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () => await _repository.GetByProviderAsync(""));
    }

    [Fact]
    public async Task Upsert_WithNullState_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.UpsertAsync(null!));
    }
}
