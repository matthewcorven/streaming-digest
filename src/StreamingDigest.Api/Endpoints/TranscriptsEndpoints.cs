using Microsoft.EntityFrameworkCore;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;

namespace StreamingDigest.Api.Endpoints;

internal static class TranscriptsEndpoints
{
    public static void MapTranscriptEndpoints(this WebApplication app)
    {
        app.MapGet("/api/videos/{videoId:guid}/transcript", async (Guid videoId, StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var transcript = await context.VideoTranscripts
                .AsNoTracking()
                .Include(candidate => candidate.Cues)
                .SingleOrDefaultAsync(candidate => candidate.VideoId == videoId && candidate.IsActive, cancellationToken);

            if (transcript is null)
            {
                return Results.NotFound();
            }

            var response = new VideoTranscriptResponse(
                transcript.Id,
                transcript.VideoId,
                transcript.SourceType,
                transcript.LanguageCode,
                transcript.Cues
                    .OrderBy(cue => cue.Sequence)
                    .Select(cue => new TranscriptCueResponse(
                        cue.Id,
                        cue.Sequence,
                        cue.StartSeconds,
                        cue.EndSeconds,
                        cue.TextOriginal,
                        cue.TextOverride,
                        cue.TextOverride ?? cue.TextOriginal))
                    .ToArray());

            return Results.Ok(response);
        });
    }
}