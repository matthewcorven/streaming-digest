namespace StreamingDigest.Infrastructure.Persistence;

public static class StorageRetentionPolicy
{
    public static int ComputeObservabilityRetentionDays(long freeSpaceBytes)
    {
        if (freeSpaceBytes > 5L * 1024 * 1024 * 1024)
        {
            return 90;
        }

        if (freeSpaceBytes > 1L * 1024 * 1024 * 1024)
        {
            return 30;
        }

        return 0;
    }

    public static bool HasObservabilityRetentionWarning(int retentionDays) => retentionDays <= 0;

    public static long ComputeTempMediaMaxBytes(long freeSpaceBytes)
        => Math.Max(1_073_741_824, freeSpaceBytes / 2);

    public static long GetMaximumReadyDriveFreeSpaceBytes()
    {
        var drives = DriveInfo.GetDrives().Where(drive => drive.IsReady).ToArray();
        return drives.Length == 0
            ? 0
            : drives.Select(drive => drive.AvailableFreeSpace).DefaultIfEmpty(0).Max();
    }
}
