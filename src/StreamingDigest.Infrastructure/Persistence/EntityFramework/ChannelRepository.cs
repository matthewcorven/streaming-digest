using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class ChannelRepository(
    StreamingDigestDbContext context,
    IRetentionCleanupService retentionCleanupService) : IChannelRepository
{
    public async Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.Channels.FirstOrDefaultAsync(channel => channel.Id == id, cancellationToken);

    public async Task<Channel?> GetByYoutubeChannelIdAsync(string youtubeChannelId, CancellationToken cancellationToken = default)
        => await context.Channels.FirstOrDefaultAsync(channel => channel.YoutubeChannelId == youtubeChannelId, cancellationToken);

    public async Task<List<Channel>> GetAllAsync(bool excludePaused = false, CancellationToken cancellationToken = default)
    {
        var query = context.Channels.AsQueryable();
        if (excludePaused)
        {
            query = query.Where(c => !c.IsPaused);
        }

        return await query.OrderBy(c => c.NameOriginal).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        await context.Channels.AddAsync(channel, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        context.Channels.Update(channel);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => DeleteAsync(id, purgeMedia: false, cancellationToken);

    public async Task DeleteAsync(Guid id, bool purgeMedia, CancellationToken cancellationToken = default)
    {
        var channel = await context.Channels.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
        if (channel is null)
        {
            return;
        }

        var videoIds = await context.Videos
            .Where(video => video.ChannelId == id)
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);

        if (purgeMedia)
        {
            if (videoIds.Count > 0)
            {
                await retentionCleanupService.PurgeOwnedArtifactsAsync(MediaArtifactOwnerTypes.Video, videoIds, cancellationToken);
            }

            await retentionCleanupService.PurgeOwnedArtifactsAsync(MediaArtifactOwnerTypes.Channel, [id], cancellationToken);
        }

        var videos = await context.Videos
            .Where(video => video.ChannelId == id)
            .ToListAsync(cancellationToken);

        context.Videos.RemoveRange(videos);
        context.Channels.Remove(channel);
        await context.SaveChangesAsync(cancellationToken);
    }
}
