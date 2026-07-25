using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class ChannelRepository(StreamingDigestDbContext context) : IChannelRepository
{
    public async Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await context.Channels.FirstOrDefaultAsync(channel => channel.Id == id, cancellationToken);

    public async Task<Channel?> GetByYoutubeChannelIdAsync(string youtubeChannelId, CancellationToken cancellationToken = default)
        => await context.Channels.FirstOrDefaultAsync(channel => channel.YoutubeChannelId == youtubeChannelId, cancellationToken);

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

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var channel = await context.Channels.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
        if (channel is null)
        {
            return;
        }

        context.Channels.Remove(channel);
        await context.SaveChangesAsync(cancellationToken);
    }
}
