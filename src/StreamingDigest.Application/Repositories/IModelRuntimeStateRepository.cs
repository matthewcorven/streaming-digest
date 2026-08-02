using StreamingDigest.Application.Models;

namespace StreamingDigest.Application.Repositories;

public interface IModelRuntimeStateRepository
{
    Task UpsertAsync(ModelRuntimeState state, CancellationToken cancellationToken = default);
    Task<ModelRuntimeState?> GetByProviderAndModelIdAsync(string provider, string modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelRuntimeState>> GetByProviderAsync(string provider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelRuntimeState>> GetAllAsync(CancellationToken cancellationToken = default);
}
