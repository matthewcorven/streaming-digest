using StreamingDigest.UnitTests.Fixtures;

namespace StreamingDigest.UnitTests;

public class FixtureLibraryTests
{
    [Fact]
    public void FixtureLibrary_ContainsTheDocumentedFixtureSet()
    {
        var fixtureLoader = new FixtureLoader();
        var fixturePaths = new[]
        {
            "ytdlp/channel-metadata.json",
            "ytdlp/video-metadata.json",
            "captions/transcript-with-timestamps.srt",
            "audio/tiny-sample.wav",
            "video/test-screenshot.mp4",
            "web/index.html",
            "web/robots.txt",
            "github/repo-metadata.json",
            "github/README.md",
            "github/LICENSE",
            "github/repo-with-readme/metadata.json",
            "github/repo-with-readme/README.md",
            "github/repo-with-readme/LICENSE",
            "github/repo-without-docs/metadata.json",
            "deepwiki/reachable-page.html",
            "deepwiki/index-your-code-placeholder.html",
            "rate-limit/youtube-429.json",
            "rate-limit/repository-429.json",
            "rate-limit/deepwiki-429.json",
            "rate-limit/website-429.json",
            "url-normalization/corpus.json",
            "recall/vague-query-corpus.json"
        };

        foreach (var fixturePath in fixturePaths)
        {
            var resolvedPath = fixtureLoader.Resolve(fixturePath);
            Assert.True(File.Exists(resolvedPath), $"Fixture '{fixturePath}' was not found.");
            var content = fixtureLoader.ReadText(fixturePath);
            Assert.False(string.IsNullOrWhiteSpace(content), $"Fixture '{fixturePath}' was empty.");
        }
    }
}
