using System.Text.Json;
using StreamingDigest.Application.Models;
using StreamingDigest.Application.Repositories;
using StreamingDigest.Domain;

namespace StreamingDigest.Application;

/// <summary>
/// WS-3 startup reconcile: hydrates <c>model_runtime_state</c> from the model runtime's
/// <c>/api/tags</c> so bootstrap-installed models show <c>ready</c> without a manual verify
/// (plan §9.3, seam S10). Runs once at API and worker startup.
///
/// Mapping rules (unit-tested):
/// <list type="bullet">
/// <item>Each Ollama model reported present by <c>/api/tags</c> is upserted.</item>
/// <item>When no state row exists, or the row is in a terminal idle state
/// (<c>ready</c>/<c>failed</c>/<c>missing</c>) and the model is present, the row becomes
/// <c>ready</c> with <c>last_seen_in_runtime_at</c> set; <c>last_verified_at</c> is set when
/// the row transitions into <c>ready</c>.</item>
/// <item>Rows in an in-flight state (<c>queued</c>/<c>running</c>/<c>downloading</c>) are NOT
/// flipped to <c>ready</c>; only <c>last_seen_in_runtime_at</c> is refreshed so a concurrent
/// download operation is not clobbered.</item>
/// <item>Models present in the runtime but absent from the catalog (user side-pulls) are
/// recorded with <c>runtime_role="unknown"</c> so they render as external models.</item>
/// <item>Non-Ollama providers are never reconciled here (verify-only per WS-0).</item>
/// </list>
/// The service never throws for an unreachable runtime or a persistence failure; it returns
/// a <see cref="ModelRuntimeReconcileResult"/> describing the outcome so startup can log and
/// signal instead of silently degrading (2026-08-01 user directive).
/// </summary>
public sealed class ModelRuntimeReconcileService
{
    private static readonly JsonSerializerOptions DetailsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> InFlightStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "running",
        "downloading"
    };

    private readonly IModelRuntimeClient _runtimeClient;

    public ModelRuntimeReconcileService(IModelRuntimeClient runtimeClient)
    {
        _runtimeClient = runtimeClient ?? throw new ArgumentNullException(nameof(runtimeClient));
    }

    public async Task<ModelRuntimeReconcileResult> ReconcileAsync(
        IModelRuntimeStateRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        IReadOnlyList<ModelPresence> installed;
        try
        {
            installed = await _runtimeClient.ListInstalledModelsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return ModelRuntimeReconcileResult.RuntimeUnreachable(ex.Message);
        }

        var reconciledAt = DateTimeOffset.UtcNow;
        var ollamaInstalled = installed
            .Where(p => string.Equals(p.Provider, ModelCatalog.ToProviderName(ModelProvider.Ollama), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var markedReady = 0;
        var refreshedInFlight = 0;
        var skipped = 0;
        string? firstPersistError = null;

        foreach (var presence in ollamaInstalled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var outcome = await ReconcileOneAsync(repository, presence, reconciledAt, cancellationToken);
                switch (outcome)
                {
                    case ReconcileOutcome.MarkedReady:
                        markedReady++;
                        break;
                    case ReconcileOutcome.RefreshedInFlight:
                        refreshedInFlight++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                firstPersistError ??= $"model '{presence.ModelId}': {ex.Message}";
                skipped++;
            }
        }

        return new ModelRuntimeReconcileResult(
            Succeeded: firstPersistError is null,
            RuntimeReachable: true,
            PresentCount: ollamaInstalled.Count,
            MarkedReadyCount: markedReady,
            RefreshedInFlightCount: refreshedInFlight,
            SkippedCount: skipped,
            ErrorSummary: firstPersistError);
    }

    private async Task<ReconcileOutcome> ReconcileOneAsync(
        IModelRuntimeStateRepository repository,
        ModelPresence presence,
        DateTimeOffset reconciledAt,
        CancellationToken cancellationToken)
    {
        var provider = ModelCatalog.ToProviderName(ModelProvider.Ollama);
        var catalogEntry = ResolveCatalogEntry(presence.ModelId);
        var persistedModelId = catalogEntry?.Id ?? presence.ModelId;
        var runtimeRole = catalogEntry is null
            ? "unknown"
            : ModelCatalog.ToRuntimeRoleName(catalogEntry.RuntimeRole);

        var existing = await repository.GetByProviderAndModelIdAsync(provider, persistedModelId, cancellationToken);
        var detailsJson = BuildDetailsJson(presence, catalogEntry);

        if (existing is not null && InFlightStatuses.Contains(existing.Status))
        {
            existing.LastSeenInRuntimeAt = reconciledAt;
            existing.DetailsJson = detailsJson ?? existing.DetailsJson;
            existing.UpdatedAt = reconciledAt;
            await repository.UpsertAsync(existing, cancellationToken);
            return ReconcileOutcome.RefreshedInFlight;
        }

        var transitionedToReady = existing is null || !string.Equals(existing.Status, "ready", StringComparison.OrdinalIgnoreCase);

        var state = existing ?? new ModelRuntimeState
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ModelId = persistedModelId,
            RuntimeRole = runtimeRole,
            Status = "ready",
            UpdatedAt = reconciledAt
        };

        state.RuntimeRole = runtimeRole;
        state.Status = "ready";
        state.CurrentOperationId = null;
        state.ProgressPercent = null;
        state.LastErrorSummary = null;
        state.LastSeenInRuntimeAt = reconciledAt;
        if (transitionedToReady)
        {
            state.LastVerifiedAt = reconciledAt;
        }

        state.DetailsJson = detailsJson ?? state.DetailsJson;
        state.UpdatedAt = reconciledAt;

        await repository.UpsertAsync(state, cancellationToken);
        return ReconcileOutcome.MarkedReady;
    }

    private static ModelOptionDefinition? ResolveCatalogEntry(string runtimeModelId)
    {
        var exact = ModelCatalog.FindById(runtimeModelId);
        if (exact is not null)
        {
            return exact;
        }

        return ModelCatalog.SupportedModels.FirstOrDefault(model =>
            runtimeModelId.StartsWith($"{model.Id}:", StringComparison.OrdinalIgnoreCase));
    }

    private static string? BuildDetailsJson(ModelPresence presence, ModelOptionDefinition? catalogEntry)
    {
        if (presence.Digest is null && presence.SizeInBytes is null && catalogEntry is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["digest"] = presence.Digest,
            ["sizeInBytes"] = presence.SizeInBytes,
            ["label"] = catalogEntry?.Label,
            ["inCatalog"] = catalogEntry is not null,
            ["reconciledFrom"] = "startup"
        }, DetailsJsonOptions);
    }

    private enum ReconcileOutcome
    {
        MarkedReady,
        RefreshedInFlight,
        Skipped
    }
}

/// <summary>Outcome summary of one startup reconcile pass.</summary>
public sealed record ModelRuntimeReconcileResult(
    bool Succeeded,
    bool RuntimeReachable,
    int PresentCount,
    int MarkedReadyCount,
    int RefreshedInFlightCount,
    int SkippedCount,
    string? ErrorSummary)
{
    public static ModelRuntimeReconcileResult RuntimeUnreachable(string? error)
        => new(false, false, 0, 0, 0, 0, error);
}
