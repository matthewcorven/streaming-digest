using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Admin;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class EfCoreAdminOperationStore(StreamingDigestDbContext dbContext) : IAdminOperationStore
{
    public async Task PersistOperationAsync(AdminActionStatus operation, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Operations.FindAsync([operation.OperationId], cancellationToken);
        if (entity is null)
        {
            entity = new OperationRecord
            {
                Id = operation.OperationId,
                CreatedAt = operation.CreatedAt,
                UpdatedAt = operation.UpdatedAt
            };
            dbContext.Operations.Add(entity);
        }

        entity.OperationType = operation.OperationType;
        entity.Status = operation.Status;
        entity.StartedAt = operation.CreatedAt;
        entity.CompletedAt = operation.Status is "completed" or "failed" or "error" ? operation.UpdatedAt : null;
        entity.SummaryJson = JsonSerializer.Serialize(new
        {
            message = operation.Message,
            target = operation.Target,
            jobId = operation.JobId,
            healthStatus = operation.HealthStatus,
            updatedAt = operation.UpdatedAt
        });
        entity.ErrorSummary = operation.Status is "failed" or "error" ? operation.Message : null;
        entity.UpdatedAt = operation.UpdatedAt;
        entity.CreatedAt = entity.CreatedAt == default ? operation.CreatedAt : entity.CreatedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminActionStatus?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Operations.AsNoTracking().SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var summary = entity.SummaryJson is null
            ? null
            : JsonSerializer.Deserialize<OperationSummary>(entity.SummaryJson);

        return new AdminActionStatus(
            entity.Id,
            entity.OperationType,
            entity.Status,
            summary?.Message ?? string.Empty,
            summary?.Target,
            summary?.JobId,
            summary?.HealthStatus,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    private sealed record OperationSummary(string? Message, string? Target, string? JobId, string? HealthStatus);
}
