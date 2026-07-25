using StreamingDigest.Domain;

namespace StreamingDigest.MatrixNotifier;

public sealed class MatrixNotificationClient
{
    public string BuildPreview(Video video, string message)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return $"[matrix] {video.DisplayTitle}: {message}";
    }
}