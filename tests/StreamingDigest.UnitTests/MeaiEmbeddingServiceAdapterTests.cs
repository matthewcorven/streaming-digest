using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;
using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public class MeaiEmbeddingServiceAdapterTests
{
    [Fact]
    public async Task GenerateEmbeddingAsync_ReturnsEmbeddingWithCorrectDimensions()
    {
        // Arrange
        const string testText = "test text";
        const int expectedDimensions = 768;
        var mockEmbedding = new Embedding<float>(new ReadOnlyMemory<float>(Enumerable.Repeat(0.5f, expectedDimensions).ToArray()));

        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ReturnsAsync(new[] { mockEmbedding }.ToAsyncEnumerable());

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object);

        // Act
        var result = await adapter.GenerateEmbeddingAsync(testText);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedDimensions, result.Dimensions);
        Assert.Equal(expectedDimensions, result.Vector.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ConvertsFloatArrayToDoubleArray()
    {
        // Arrange
        const string testText = "test text";
        var floatValues = new[] { 0.1f, 0.2f, 0.3f };
        var mockEmbedding = new Embedding<float>(new ReadOnlyMemory<float>(floatValues));

        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ReturnsAsync(new[] { mockEmbedding }.ToAsyncEnumerable());

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object);

        // Act
        var result = await adapter.GenerateEmbeddingAsync(testText);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Vector.Length);
        Assert.Equal(0.1, result.Vector[0], precision: 5);
        Assert.Equal(0.2, result.Vector[1], precision: 5);
        Assert.Equal(0.3, result.Vector[2], precision: 5);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ThrowsWhenDimensionsMismatch()
    {
        // Arrange
        const string testText = "test text";
        const int expectedDimensions = 768;
        var actualDimensions = 512;
        var floatValues = Enumerable.Repeat(0.5f, actualDimensions).ToArray();
        var mockEmbedding = new Embedding<float>(new ReadOnlyMemory<float>(floatValues));

        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ReturnsAsync(new[] { mockEmbedding }.ToAsyncEnumerable());

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object, expectedDimensions);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GenerateEmbeddingAsync(testText));
        Assert.Contains("dimension", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_PreservesProvider()
    {
        // Arrange
        const string testText = "test text";
        var mockEmbedding = new Embedding<float>(new ReadOnlyMemory<float>(Enumerable.Repeat(0.5f, 768).ToArray()));

        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ReturnsAsync(new[] { mockEmbedding }.ToAsyncEnumerable());

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object, provider: "test-provider", model: "test-model");

        // Act
        var result = await adapter.GenerateEmbeddingAsync(testText);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-provider", result.Provider);
        Assert.Equal("test-model", result.Model);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_ThrowsOnException()
    {
        // Arrange
        const string testText = "test text";
        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => adapter.GenerateEmbeddingAsync(testText));
        Assert.Contains("Connection failed", ex.Message);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_HandlesNullOrWhiteSpace()
    {
        // Arrange
        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GenerateEmbeddingAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GenerateEmbeddingAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.GenerateEmbeddingAsync("   "));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SendsCorrectTextToGenerator()
    {
        // Arrange
        const string testText = "test text to embed";
        var mockEmbedding = new Embedding<float>(new ReadOnlyMemory<float>(Enumerable.Repeat(0.5f, 768).ToArray()));

        var mockGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockGenerator
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions>(),
                default))
            .ReturnsAsync(new[] { mockEmbedding }.ToAsyncEnumerable());

        var adapter = new MeaiEmbeddingServiceAdapter(mockGenerator.Object);

        // Act
        await adapter.GenerateEmbeddingAsync(testText);

        // Assert
        mockGenerator.Verify(
            g => g.GenerateAsync(
                It.Is<IEnumerable<string>>(texts => texts.Single() == testText),
                It.IsAny<EmbeddingGenerationOptions>(),
                default),
            Times.Once);
    }
}
