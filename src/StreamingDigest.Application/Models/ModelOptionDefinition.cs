using StreamingDigest.Domain;

namespace StreamingDigest.Application.Models;

/// <summary>
/// One supported model option as projected by the catalog. Used by the API surface
/// (model options / verify / download queueing) and by runtime reconciliation.
/// </summary>
public sealed record ModelOptionDefinition(
    string Id,
    string Family,
    string Status,
    string Label,
    ModelProvider Provider,
    RuntimeRole RuntimeRole,
    bool Downloadable,
    string? InstallCommand = null,
    string? MountPath = null);
