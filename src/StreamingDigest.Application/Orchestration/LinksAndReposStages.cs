using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;

namespace StreamingDigest.Application.Orchestration;

/// <summary>
/// Stage 4 — link extraction + classification. Extracts links from the video
/// description and classifies each. Classification is guarded: when the LLM
/// capability is unready the service's rule-based heuristic fallback is used and a
/// notification event is emitted exactly once for the stage. Extracted links become
/// <see cref="ExternalResource"/> rows (added to the context's resource list for the
/// pipeline to persist).
/// </summary>
public sealed class LinksStageHandler(
    IVideoLinkExtractionService linkExtraction,
    ILinkClassificationService linkClassification,
    IModelReadinessGuard modelReadinessGuard,
    ILogger<LinksStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Links;

    public async Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        var links = linkExtraction.Extract(
            context.Video.DescriptionOverride ?? context.Video.DescriptionOriginal,
            pinnedCommentText: null);

        if (links.Count == 0)
        {
            // No links — trivially succeeded.
            return;
        }

        var llmReady = await modelReadinessGuard.IsReadyAsync(ModelCapabilities.Llm, cancellationToken);
        var heuristicNotified = false;

        foreach (var link in links)
        {
            var classification = linkClassification.Classify(link.Url);
            if (!llmReady)
            {
                if (string.Equals(classification.Method, "rule", StringComparison.Ordinal) && !heuristicNotified)
                {
                    heuristicNotified = true;
                    context.Warnings.Add("links: LLM unready; heuristic classification fallback used");
                    context.PendingEvents.Add(StageNotification.CapabilityUnready(
                        ModelCapabilities.Llm, StageName, "heuristic link classification fallback used", context));
                }
            }

            var resource = new ExternalResource
            {
                CanonicalUrl = link.Url,
                Domain = TryGetHost(link.Url),
                ResourceType = MapClassificationToResourceType(classification.Classification),
                ClassificationOriginal = classification.Classification.ToString(),
                ClassificationConfidence = (decimal)classification.Confidence,
                ClassificationMethod = classification.Method,
                IsAdOrSponsor = classification.Classification is LinkClassification.AdSponsor or LinkClassification.Affiliate,
            };
            context.Resources.Add(resource);
        }

        logger.LogDebug(
            "Video {VideoId}: extracted {Count} links ({AdCount} ad/sponsor)",
            context.Video.YoutubeVideoId, context.Resources.Count,
            context.Resources.Count(r => r.IsAdOrSponsor));
    }

    private static string? TryGetHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    internal static string MapClassificationToResourceType(LinkClassification classification)
        => classification switch
        {
            LinkClassification.CodeRepository => "repository",
            LinkClassification.WebsiteResource => "website",
            LinkClassification.AdSponsor => "ad_sponsor",
            LinkClassification.Affiliate => "affiliate",
            LinkClassification.Social => "social",
            LinkClassification.Newsletter => "newsletter",
            LinkClassification.Course => "course",
            LinkClassification.Merch => "merch",
            LinkClassification.Other => "other",
            _ => "unknown",
        };
}

/// <summary>
/// Stage 5 — repository metadata. Fetches metadata for each repository-classified
/// resource and materializes <see cref="RepositoryRecord"/> rows. Rate limiting and
/// fetch failures degrade the resource to a warning; they never fail the video.
/// </summary>
public sealed class ReposStageHandler(
    IRepositoryMetadataService repositoryMetadata,
    ILogger<ReposStageHandler> logger) : IVideoStageHandler
{
    public string StageName => IngestionStageNames.Repos;

    public Task ExecuteAsync(VideoPipelineContext context, CancellationToken cancellationToken)
    {
        var repoResources = context.Resources
            .Where(r => r.ResourceType == "repository")
            .ToList();

        foreach (var resource in repoResources)
        {
            var result = repositoryMetadata.Fetch(resource.CanonicalUrl);
            if (result.IsRateLimited)
            {
                context.Warnings.Add($"repos: rate limited fetching {resource.CanonicalUrl}");
                continue;
            }

            if (!result.IsSuccess || result.Metadata is null)
            {
                context.Warnings.Add($"repos: fetch failed for {resource.CanonicalUrl}: {result.ErrorMessage ?? "unknown"}");
                continue;
            }

            var metadata = result.Metadata;
            var record = new RepositoryRecord
            {
                Host = metadata.Host,
                CanonicalUrl = metadata.CanonicalUrl,
                Owner = metadata.Owner,
                Name = metadata.RepositoryName,
                NormalizedOwner = metadata.Owner?.ToLowerInvariant(),
                NormalizedName = metadata.RepositoryName?.ToLowerInvariant(),
                DefaultBranch = metadata.DefaultBranch,
                DescriptionOriginal = metadata.Description,
                Stars = metadata.Stars,
                PrimaryLanguage = metadata.Language,
                LicenseSpdxId = metadata.LicenseName,
                DeepwikiUrl = metadata.DeepWikiUrl,
            };
            context.Repositories.Add(record);

            // Enrich the resource with repository metadata for search documents.
            resource.TitleOriginal ??= metadata.RepositoryName;
            resource.DescriptionOriginal ??= metadata.Description;

            logger.LogDebug(
                "Video {VideoId}: repository {Owner}/{Name} ({Stars} stars)",
                context.Video.YoutubeVideoId, metadata.Owner, metadata.RepositoryName, metadata.Stars);
        }

        return Task.CompletedTask;
    }
}
