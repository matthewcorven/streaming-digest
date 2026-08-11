using StreamingDigest.Domain;

namespace StreamingDigest.Application.Repositories;

/// <summary>
/// Persistence seam for the <c>operations</c> audit/correlation table. Unlike the admin
/// operation store, writes here are mandatory: the download handoff must not return 202
/// unless the operation row actually persisted.
/// </summary>
public interface IOperationStore
{
    /// <summary>Persists (inserts or updates) an operation record.</summary>
    Task PersistAsync(OperationRecord operation, CancellationToken cancellationToken = default);

    /// <summary>Loads a single operation by id, or null when it does not exist.</summary>
    Task<OperationRecord?> GetByIdAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Stamps the Hangfire job id onto a persisted operation.</summary>
    Task UpdateHangfireJobIdAsync(Guid operationId, string hangfireJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all operations of the given <paramref name="operationType"/> whose status
    /// is <c>queued</c> or <c>running</c> (i.e. not yet in a terminal state).
    /// </summary>
    Task<List<OperationRecord>> GetActiveByTypeAsync(string operationType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most-recently-completed operation of the given
    /// <paramref name="operationType"/>, or <c>null</c> when none exists.
    /// </summary>
    Task<OperationRecord?> GetLastCompletedByTypeAsync(string operationType, CancellationToken cancellationToken = default);
}
