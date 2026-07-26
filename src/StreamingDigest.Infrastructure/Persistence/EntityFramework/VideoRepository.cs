using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class VideoRepository(
    StreamingDigestDbContext context,
    IRetentionCleanupService retentionCleanupService) : IVideoRepository
{
    public async Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.Videos.FirstOrDefaultAsync(video => video.Id == id, cancellationToken);

    public async Task<Video?> GetByPlatformIdentityAsync(string platform, string platformVideoId, CancellationToken cancellationToken = default)
        => await context.Videos.FirstOrDefaultAsync(video => video.Platform == platform && video.PlatformVideoId == platformVideoId, cancellationToken);

    public async Task AddAsync(Video video, CancellationToken cancellationToken = default)
    {
        await context.Videos.AddAsync(video, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Video video, CancellationToken cancellationToken = default)
    {
        context.Videos.Update(video);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => DeleteAsync(id, purgeMedia: false, cancellationToken);

    public async Task DeleteAsync(Guid id, bool purgeMedia, CancellationToken cancellationToken = default)
    {
        var video = await context.Videos.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
        if (video is null)
        {
            return;
        }

        if (purgeMedia)
        {
            await retentionCleanupService.PurgeOwnedArtifactsAsync(MediaArtifactOwnerTypes.Video, [id], cancellationToken);
        }

        context.Videos.Remove(video);
        await context.SaveChangesAsync(cancellationToken);
    }
}
