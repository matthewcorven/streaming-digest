using StreamingDigest.Application.Admin;

namespace StreamingDigest.UnitTests;

public sealed class AdminOperationsServiceTests
{
    [Fact]
    public async Task RunIngestionNowAsync_ReturnsAcceptedOperation()
    {
        var service = new AdminOperationsService();

        var result = await service.RunIngestionNowAsync("channel-1");

        Assert.Equal("accepted", result.Status);
        Assert.Equal("ingestion.run", result.OperationType);
        Assert.Equal("channel-1", result.Target);
        Assert.NotEqual(Guid.Empty, result.OperationId);

        var operation = await service.GetOperationAsync(result.OperationId);
        Assert.NotNull(operation);
        Assert.Equal(result.OperationType, operation!.OperationType);
    }

    [Fact]
    public async Task TestEmbeddingServiceAsync_ReturnsCompletedHealthResult()
    {
        var service = new AdminOperationsService();

        var result = await service.TestEmbeddingServiceAsync();

        Assert.Equal("completed", result.Status);
        Assert.Equal("test.embeddings", result.OperationType);
        Assert.Equal("healthy", result.HealthStatus);

        var operation = await service.GetOperationAsync(result.OperationId);
        Assert.NotNull(operation);
        Assert.Equal("healthy", operation!.HealthStatus);
    }
}
