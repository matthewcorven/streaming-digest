using System.ComponentModel;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingDigest.Application.Transcripts;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Transcripts;

public sealed class YtDlpVideoMediaSourceResolver(
    IStreamingDigestDbContext context,
    ILogger<YtDlpVideoMediaSourceResolver> logger) : IVideoMediaSourceResolver
{
    private const string YtDlpPathEnvironmentVariable = "STREAMINGDIGEST_YT_DLP_PATH";

    public async Task<ResolvedMediaFile?> ResolveAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await context.Videos
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == videoId, cancellationToken)
            ?? throw new InvalidOperationException($"Video '{videoId}' was not found.");

        var sourceUrl = ResolveSourceUrl(video);
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new InvalidOperationException($"Video '{videoId}' does not have a source URL that can be used for transcript fallback.");
        }

        var outputTemplate = Path.Combine(
            Path.GetTempPath(),
            $"streaming-digest-transcript-{videoId:N}-{Guid.NewGuid():N}.%(ext)s");

        var ytDlpPath = Environment.GetEnvironmentVariable(YtDlpPathEnvironmentVariable);
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(ytDlpPath) ? "yt-dlp" : ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--no-progress");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("bestaudio/best");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputTemplate);
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("after_move:filepath");
        startInfo.ArgumentList.Add(sourceUrl);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("yt-dlp could not be started.");

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var standardOutput = (await standardOutputTask).Trim();
            var standardError = (await standardErrorTask).Trim();

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                var message = string.IsNullOrWhiteSpace(details)
                    ? $"yt-dlp exited with code {process.ExitCode}."
                    : $"yt-dlp exited with code {process.ExitCode}: {details}";
                throw new InvalidOperationException(message);
            }

            var downloadedPath = standardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();

            if (string.IsNullOrWhiteSpace(downloadedPath) || !File.Exists(downloadedPath))
            {
                throw new InvalidOperationException("yt-dlp completed without producing a media file for transcript fallback.");
            }

            logger.LogInformation(
                "Resolved transcript fallback media for video {VideoId} to {MediaFilePath} via yt-dlp.",
                videoId,
                downloadedPath);

            return new ResolvedMediaFile(downloadedPath, DeleteWhenFinished: true);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Unable to invoke yt-dlp for transcript fallback: {ex.Message}", ex);
        }
    }

    private static string ResolveSourceUrl(Video video)
        => !string.IsNullOrWhiteSpace(video.VideoUrl) ? video.VideoUrl
            : !string.IsNullOrWhiteSpace(video.PlatformVideoUrl) ? video.PlatformVideoUrl
            : !string.IsNullOrWhiteSpace(video.YoutubeVideoId) ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(video.YoutubeVideoId)}"
            : string.Empty;
}
