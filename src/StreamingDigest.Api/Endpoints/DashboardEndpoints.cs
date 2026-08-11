using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;
using StreamingDigest.Infrastructure.Persistence.EntityFramework;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/internal/dashboard", async (StreamingDigestDbContext context, CancellationToken cancellationToken) =>
        {
            var channelCount = await context.Channels.CountAsync(cancellationToken);
            var videoCount = await context.Videos.CountAsync(cancellationToken);

            var latestRun = await context.IngestionRuns
                .OrderByDescending(run => run.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            Digest? latestDigest = null;
            if (latestRun is not null)
            {
                latestDigest = await context.Digests
                    .Where(digest => digest.IngestionRunId == latestRun.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var failedItemCount = latestRun is not null
                ? await context.IngestionItems
                    .Where(item => item.IngestionRunId == latestRun.Id && item.Status == "failed")
                    .CountAsync(cancellationToken)
                : 0;

            var deferredItemCount = latestRun is not null
                ? await context.IngestionItems
                    .Where(item => item.IngestionRunId == latestRun.Id && item.Status == "deferred")
                    .CountAsync(cancellationToken)
                : 0;

            var summary = DashboardReadModelMapper.MapToSummary(
                channelCount, videoCount, latestRun, latestDigest, failedItemCount, deferredItemCount);

            return Results.Ok(summary);
        });
    }
}
