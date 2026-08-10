using StreamingDigest.Domain;

namespace StreamingDigest.Application.Repositories;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Channel?> GetByYoutubeChannelIdAsync(string youtubeChannelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all channels. When <paramref name="excludePaused"/> is <c>true</c>, channels
    /// with <see cref="Channel.IsPaused"/> set are omitted.
    /// </summary>
    Task<List<Channel>> GetAllAsync(bool excludePaused = false, CancellationToken cancellationToken = default);

    Task AddAsync(Channel channel, CancellationToken cancellationToken = default);
    Task UpdateAsync(Channel channel, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, bool purgeMedia, CancellationToken cancellationToken = default);
}
