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
}
