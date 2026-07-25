using StreamingDigest.Application;
using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public class VideoQueryServiceTests
{
    [Fact]
    public void Describe_UsesDisplayTitle()
    {
        var video = new Video(Guid.NewGuid(), "  Sample video  ");
        var service = new VideoQueryService();

        Assert.Equal("Ready to process Sample video", service.Describe(video));
    }
}
