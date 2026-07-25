using StreamingDigest.Application.Screenshots;
using StreamingDigest.UnitTests.Fixtures;

namespace StreamingDigest.UnitTests;

public class ScreenshotGenerationServiceTests
{
    private readonly FixtureLoader _fixtureLoader = new();

    [Fact]
    public async Task GenerateAsync_WithFixtureAndTempOutput_CreatesWebpFile()
    {
        var inputPath = _fixtureLoader.Resolve("video/test-screenshot.mp4");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "streaming-digest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "thumbnail.webp");

        var shimDirectory = Path.Combine(outputDirectory, "shims");
        Directory.CreateDirectory(shimDirectory);
        var ffmpegShimPath = Path.Combine(shimDirectory, "ffmpeg");
        await File.WriteAllTextAsync(ffmpegShimPath, "#!/bin/sh\nset -eu\noutput_path=\"\"\nfor arg in \"$@\"; do\n  output_path=\"$arg\"\ndone\nif [ -n \"$output_path\" ]; then\n  printf 'stub screenshot' > \"$output_path\"\nfi\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ffmpegShimPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var service = new ScreenshotGenerationService();
        var result = await service.GenerateAsync(new ScreenshotGenerationRequest(inputPath, outputPath, OffsetSeconds: 1, FfmpegPath: ffmpegShimPath));

        try
        {
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(File.Exists(outputPath), "Expected the WebP screenshot to be written to disk.");
            Assert.Equal(".webp", Path.GetExtension(outputPath), ignoreCase: true);
            Assert.True(new FileInfo(outputPath).Length > 0, "Expected the generated screenshot to have content.");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateAsync_WithMissingInput_ReturnsFailureResult()
    {
        var service = new ScreenshotGenerationService();
        var result = await service.GenerateAsync(new ScreenshotGenerationRequest("/does/not/exist.mp4", "/tmp/thumbnail.webp"));

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.ErrorMessage);
        Assert.Equal("screenshot_file_missing", result.DomainEventType);
    }
}
