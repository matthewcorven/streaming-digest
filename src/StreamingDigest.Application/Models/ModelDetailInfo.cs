namespace StreamingDigest.Application.Models;

/// <summary>
/// Detailed model metadata from <c>/api/show</c>.
/// </summary>
public sealed record ModelDetailInfo(
    string? Modelfile,
    string? Parameters,
    string? Template,
    string? DetailsJson);
