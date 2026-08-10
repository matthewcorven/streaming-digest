using StreamingDigest.Application.Models;
using StreamingDigest.Domain;

namespace StreamingDigest.Infrastructure.Persistence;

public sealed class ModelDiscoveryService
{
    private static readonly IReadOnlyList<ModelOptionDefinition> SupportedModels = ModelCatalog.SupportedModels;

    private readonly AppReadinessStateService _readinessStateService;

    public ModelDiscoveryService(AppReadinessStateService readinessStateService)
    {
        _readinessStateService = readinessStateService;
    }

    public IReadOnlyList<ModelOptionDefinition> GetSupportedModels() => SupportedModels.ToList();

    public async Task<ModelDownloadResult> QueueDownloadAsync(string connectionString, string? modelKind, string? modelId, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelKind, modelId);
        var operationId = Guid.NewGuid();
        var statusUrl = $"/api/admin/operations/{operationId}";

        await RecordReadinessVerificationAsync(connectionString, model, cancellationToken, "queued");

        return new ModelDownloadResult("queued", model.Family, model.Id, operationId, statusUrl);
    }

    public async Task<ModelVerificationResult> VerifyModelAsync(string connectionString, string? modelKind, string? modelId, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelKind, modelId);
        await RecordReadinessVerificationAsync(connectionString, model, cancellationToken, "succeeded");

        return new ModelVerificationResult("verified", model.Family, model.Id, true, $"{model.Label} is ready for onboarding.");
    }

    private static ModelOptionDefinition ResolveModel(string? modelKind, string? modelId)
    {
        var normalizedKind = Normalize(modelKind);
        var normalizedModelId = Normalize(modelId);

        if (!string.IsNullOrWhiteSpace(normalizedKind))
        {
            var byKind = SupportedModels.FirstOrDefault(model => string.Equals(model.Family, normalizedKind, StringComparison.OrdinalIgnoreCase));
            if (byKind is not null)
            {
                return byKind;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedModelId))
        {
            var byId = SupportedModels.FirstOrDefault(model => string.Equals(model.Id, normalizedModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        throw new ArgumentException($"The requested model '{modelId ?? "(null)"}' is not currently supported.", nameof(modelId));
    }

    private async Task RecordReadinessVerificationAsync(string connectionString, ModelOptionDefinition model, CancellationToken cancellationToken, string status)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var stepKey = ResolveReadinessStepKey(model);

        try
        {
            await _readinessStateService.VerifyStepAsync(
                connectionString,
                stepKey,
                new OnboardingStepVerificationRequest(
                    status,
                    null,
                    new Dictionary<string, object?>
                    {
                        ["provider"] = model.Provider.ToString().ToLowerInvariant(),
                        ["modelId"] = model.Id,
                        ["modelKind"] = model.Family,
                        ["modelLabel"] = model.Label,
                        ["requestedAt"] = DateTimeOffset.UtcNow
                    }),
                cancellationToken);
        }
        catch
        {
            // Best-effort onboarding state updates are not required for the model discovery flow to proceed.
        }
    }

    private static string ResolveReadinessStepKey(ModelOptionDefinition model) => model.Family switch
    {
        "embedding" => "embedding_model_verified",
        "llm" => "llm_model_verified",
        "audio" => "audio_to_text_verified",
        _ => "embedding_model_verified"
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ModelDownloadResult(string Status, string ModelKind, string ModelId, Guid OperationId, string StatusUrl);

public sealed record ModelVerificationResult(string Status, string ModelKind, string ModelId, bool Verified, string Message);
