using StreamingDigest.Domain;

namespace StreamingDigest.Application;

public sealed class VideoQueryService
{
    public string Describe(Video video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return $"Ready to process {video.DisplayTitle}";
    }
}