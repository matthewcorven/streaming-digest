using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Screenshots;

public sealed record ScreenshotGenerationRequest(
    string InputFilePath,
    string OutputFilePath,
    double? OffsetSeconds = null,
    int? Quality = null,
    string? FfmpegPath = null);

public sealed record ScreenshotGenerationResult(
    bool Succeeded,
    string? OutputFilePath,
    string? ErrorMessage,
    string? DomainEventType = null);

public interface IScreenshotGenerationService
{
    Task<ScreenshotGenerationResult> GenerateAsync(ScreenshotGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed class ScreenshotGenerationService : IScreenshotGenerationService
{
    private const double DefaultOffsetSeconds = 5;
    private const int DefaultQuality = 80;

    public Task<ScreenshotGenerationResult> GenerateAsync(ScreenshotGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.InputFilePath))
        {
            return Task.FromResult(Failure("An input media file path is required.", DomainEventTypeCatalog.ScreenshotFileMissing));
        }

        if (string.IsNullOrWhiteSpace(request.OutputFilePath))
        {
            return Task.FromResult(Failure("An output screenshot path is required.", DomainEventTypeCatalog.ScreenshotFileMissing));
        }

        if (!File.Exists(request.InputFilePath))
        {
            return Task.FromResult(Failure($"The input media file '{request.InputFilePath}' does not exist.", DomainEventTypeCatalog.ScreenshotFileMissing));
        }

        var outputDirectory = Path.GetDirectoryName(request.OutputFilePath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var ffmpegPath = string.IsNullOrWhiteSpace(request.FfmpegPath) ? "ffmpeg" : request.FfmpegPath;
        var offsetSeconds = request.OffsetSeconds ?? DefaultOffsetSeconds;
        var quality = request.Quality ?? DefaultQuality;
        var outputFilePath = Path.GetFullPath(request.OutputFilePath);

        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(offsetSeconds.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(request.InputFilePath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libwebp");
            startInfo.ArgumentList.Add("-quality");
            startInfo.ArgumentList.Add(quality.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(outputFilePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(Failure("Unable to start ffmpeg.", DomainEventTypeCatalog.ScreenshotFileMissing));
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            process.WaitForExit();

            var standardOutput = standardOutputTask.GetAwaiter().GetResult();
            var standardError = standardErrorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                if (File.Exists(outputFilePath))
                {
                    File.Delete(outputFilePath);
                }

                var details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                var message = string.IsNullOrWhiteSpace(details)
                    ? $"ffmpeg exited with code {process.ExitCode}."
                    : $"ffmpeg exited with code {process.ExitCode}: {details}";
                return Task.FromResult(Failure(message, DomainEventTypeCatalog.ScreenshotFileMissing));
            }

            if (!File.Exists(outputFilePath))
            {
                return Task.FromResult(Failure("ffmpeg completed without producing a screenshot.", DomainEventTypeCatalog.ScreenshotFileMissing));
            }

            return Task.FromResult(Success(outputFilePath));
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(Failure($"Unable to invoke ffmpeg: {ex.Message}", DomainEventTypeCatalog.ScreenshotFileMissing));
        }
    }

    private static ScreenshotGenerationResult Success(string outputFilePath)
    {
        return new ScreenshotGenerationResult(true, outputFilePath, null, null);
    }

    private static ScreenshotGenerationResult Failure(string message, string domainEventType)
    {
        return new ScreenshotGenerationResult(false, null, message, domainEventType);
    }
}
