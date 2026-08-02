namespace StreamingDigest.Infrastructure.Persistence;

public interface IModelRuntimeStateSchemaGuard
{
    Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken = default);
}
