using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Channel?> GetByYoutubeChannelIdAsync(string youtubeChannelId, CancellationToken cancellationToken = default);
    Task AddAsync(Channel channel, CancellationToken cancellationToken = default);
    Task UpdateAsync(Channel channel, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
