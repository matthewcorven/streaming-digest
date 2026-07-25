using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public interface INotificationDispatchService
{
    Task<Notification> QueueDigestNotificationAsync(Digest digest, Guid? operationId, string? target, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<OutboxMessage>> DispatchPendingAsync(CancellationToken cancellationToken = default);
}
