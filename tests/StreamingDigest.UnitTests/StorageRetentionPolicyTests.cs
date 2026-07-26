using StreamingDigest.Infrastructure.Persistence;

namespace StreamingDigest.UnitTests;

public sealed class StorageRetentionPolicyTests
{
    [Theory]
    [InlineData(6L * 1024 * 1024 * 1024, 90, false)]
    [InlineData(2L * 1024 * 1024 * 1024, 30, false)]
    [InlineData(1L * 1024 * 1024 * 1024, 0, true)]
    [InlineData(0, 0, true)]
    public void ComputeObservabilityRetentionDays_UsesDiskThresholdPolicy(long freeSpaceBytes, int expectedRetentionDays, bool expectedWarning)
    {
        var retentionDays = StorageRetentionPolicy.ComputeObservabilityRetentionDays(freeSpaceBytes);

        Assert.Equal(expectedRetentionDays, retentionDays);
        Assert.Equal(expectedWarning, StorageRetentionPolicy.HasObservabilityRetentionWarning(retentionDays));
    }
}
