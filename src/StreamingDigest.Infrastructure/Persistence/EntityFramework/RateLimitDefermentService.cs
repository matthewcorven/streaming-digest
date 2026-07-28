using Microsoft.EntityFrameworkCore;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public interface IRateLimitDefermentService
{
    Task CreateDefermentAsync(
        string scopeType,
        string scopeKey,
        string reason,
        TimeSpan duration,
        string? detailsJson,
        CancellationToken cancellationToken = default);

    Task<RateLimitDeferment?> GetActiveDefermentAsync(
        string scopeType,
        string scopeKey,
        CancellationToken cancellationToken = default);

    Task<List<RateLimitDeferment>> GetActiveDefermentsByScopeTypeAsync(
        string scopeType,
        CancellationToken cancellationToken = default);

    Task<List<RateLimitDeferment>> GetExpiredDefermentsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    Task ClearDefermentAsync(
        Guid defermentId,
        CancellationToken cancellationToken = default);

    Task ExpireDefermentAsync(
        Guid defermentId,
        CancellationToken cancellationToken = default);

    Task CleanupExpiredDefermentsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class RateLimitDefermentService : IRateLimitDefermentService
{
    private readonly StreamingDigestDbContext _dbContext;

    public RateLimitDefermentService(StreamingDigestDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task CreateDefermentAsync(
        string scopeType,
        string scopeKey,
        string reason,
        TimeSpan duration,
        string? detailsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((long)duration.TotalMilliseconds);

        var now = DateTimeOffset.UtcNow;
        var retryAfter = now.Add(duration);

        var deferment = new RateLimitDeferment
        {
            Id = Guid.NewGuid(),
            ScopeType = scopeType,
            ScopeKey = scopeKey,
            Reason = reason,
            RetryAfterAt = retryAfter,
            Status = RateLimitDeferment.StatusValues.Active,
            DetailsJson = detailsJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.RateLimitDeferments.Add(deferment);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RateLimitDeferment?> GetActiveDefermentAsync(
        string scopeType,
        string scopeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        var now = DateTimeOffset.UtcNow;

        var deferment = _dbContext.RateLimitDeferments
            .Where(d => d.ScopeType == scopeType && d.ScopeKey == scopeKey)
            .AsEnumerable()
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();

        if (deferment is null || !deferment.IsActive())
        {
            return null;
        }

        if (deferment.RetryAfterAt <= now)
        {
            deferment.Expire();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        return deferment;
    }

    public async Task<List<RateLimitDeferment>> GetActiveDefermentsByScopeTypeAsync(
        string scopeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeType);

        var now = DateTimeOffset.UtcNow;

        var deferments = await _dbContext.RateLimitDeferments
            .Where(d => d.ScopeType == scopeType && d.Status == RateLimitDeferment.StatusValues.Active)
            .AsEnumerable()
            .Where(d => d.RetryAfterAt > now)
            .ToAsyncEnumerable()
            .ToListAsync(cancellationToken);

        return deferments;
    }

    public async Task<List<RateLimitDeferment>> GetExpiredDefermentsSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var deferments = await _dbContext.RateLimitDeferments
            .Where(d => d.Status == RateLimitDeferment.StatusValues.Expired || d.Status == RateLimitDeferment.StatusValues.Cleared)
            .AsEnumerable()
            .Where(d => d.UpdatedAt >= since)
            .ToAsyncEnumerable()
            .ToListAsync(cancellationToken);

        return deferments;
    }

    public async Task ClearDefermentAsync(
        Guid defermentId,
        CancellationToken cancellationToken = default)
    {
        var deferment = await _dbContext.RateLimitDeferments.FindAsync(
            new object?[] { defermentId },
            cancellationToken: cancellationToken);

        if (deferment is null)
        {
            return;
        }

        deferment.Clear();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ExpireDefermentAsync(
        Guid defermentId,
        CancellationToken cancellationToken = default)
    {
        var deferment = await _dbContext.RateLimitDeferments.FindAsync(
            new object?[] { defermentId },
            cancellationToken: cancellationToken);

        if (deferment is null)
        {
            return;
        }

        deferment.Expire();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CleanupExpiredDefermentsAsync(
        CancellationToken cancellationToken = default)
    {
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

        var expiredRecords = await _dbContext.RateLimitDeferments
            .Where(d => d.Status == RateLimitDeferment.StatusValues.Expired || d.Status == RateLimitDeferment.StatusValues.Cleared)
            .ToListAsync(cancellationToken);

        var recordsToDelete = expiredRecords.Where(d => d.UpdatedAt < thirtyDaysAgo).ToList();

        if (recordsToDelete.Any())
        {
            _dbContext.RateLimitDeferments.RemoveRange(recordsToDelete);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
