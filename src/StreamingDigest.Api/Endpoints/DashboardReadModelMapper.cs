using StreamingDigest.Domain;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Endpoints;

/// <summary>
/// Maps raw DB projections to the <see cref="DashboardSummary"/> read model.
/// Kept as a static class so the mapping logic can be exercised in unit tests
/// without spinning up a DbContext.
/// </summary>
public static class DashboardReadModelMapper
{
    public static DashboardSummary MapToSummary(
        int channelCount,
        int videoCount,
        IngestionRun? latestRun,
        Digest? latestDigest,
        int failedItemCount,
        int deferredItemCount)
    {
        var hasCorpus = videoCount > 0;
        var hasCompletedRun = latestRun?.CompletedAt is not null;

        var corpusState = new DashboardCorpusState
        {
            HasSearchableCorpus = hasCorpus,
            HasCompletedRun = hasCompletedRun,
            LatestCompletedRunFoundZeroVideos = hasCompletedRun && latestRun!.NewVideosFound == 0,
            WaitingHeadline = channelCount == 0
                ? "Add a channel to get started"
                : "No searchable content yet",
            WaitingMessage = channelCount == 0
                ? "Add at least one channel to start ingesting videos."
                : "Run ingestion to index your first videos.",
            RunNowLabel = "Run ingestion now",
            BackfillGuidance = hasCompletedRun && latestRun!.NewVideosFound == 0
                ? "Try running a backfill to import videos from further back in time."
                : string.Empty
        };

        DigestPayload? payload = null;
        if (latestDigest is not null && !string.IsNullOrWhiteSpace(latestDigest.PayloadJson))
        {
            payload = DigestPayloadSerializer.Deserialize(latestDigest.PayloadJson);
        }

        var digestSummary = BuildDigestSummary(payload, latestRun, failedItemCount, deferredItemCount);
        var pendingActions = BuildPendingActions(failedItemCount, deferredItemCount, latestRun);

        var summaryText = hasCorpus
            ? BuildSummaryText(channelCount, videoCount, latestRun)
            : string.Empty;

        return new DashboardSummary
        {
            Title = "Dashboard",
            Summary = summaryText,
            Digest = digestSummary,
            SearchLaunchpad = new DashboardSearchLaunchpad
            {
                Heading = "Search launchpad",
                Prompt = hasCorpus
                    ? $"Search across {videoCount:N0} indexed video{(videoCount == 1 ? string.Empty : "s")} in {channelCount:N0} channel{(channelCount == 1 ? string.Empty : "s")}."
                    : "No corpus available yet.",
                QueryPlaceholder = "Search your videos…"
            },
            PendingActions = pendingActions,
            Corpus = corpusState
        };
    }

    private static DashboardDigestSummary BuildDigestSummary(
        DigestPayload? payload,
        IngestionRun? latestRun,
        int failedItemCount,
        int deferredItemCount)
    {
        if (payload is null || latestRun is null)
        {
            return new DashboardDigestSummary
            {
                IsEmpty = true,
                Heading = "Daily digest",
                EmptyHeadline = "No digest yet",
                EmptyMessage = "The dashboard will populate once an ingestion run completes."
            };
        }

        var sections = new List<DashboardDigestSection>();
        var order = 0;

        if (payload.NewVideos.Count > 0)
        {
            sections.Add(new DashboardDigestSection
            {
                Order = order++,
                Key = "new-videos",
                Title = "New videos",
                Summary = $"{payload.NewVideos.Count} new video{(payload.NewVideos.Count == 1 ? string.Empty : "s")} ingested.",
                Cards = payload.NewVideos.Select(v => new DashboardDigestCard
                {
                    Title = v.Label,
                    Subtitle = string.Empty,
                    Detail = v.Detail ?? string.Empty
                }).ToList()
            });
        }

        if (payload.HighSignalMatches.Count > 0)
        {
            sections.Add(new DashboardDigestSection
            {
                Order = order++,
                Key = "high-signal",
                Title = "High-signal matches",
                Summary = $"{payload.HighSignalMatches.Count} high-signal match{(payload.HighSignalMatches.Count == 1 ? string.Empty : "es")} found.",
                Cards = payload.HighSignalMatches.Select(m => new DashboardDigestCard
                {
                    Title = m.Label,
                    Subtitle = $"{m.SimilarityPercent:F0}% match",
                    Detail = string.Empty,
                    RelativeSimilarityPercent = m.SimilarityPercent
                }).ToList()
            });
        }

        if (payload.NewResources.Count > 0)
        {
            sections.Add(new DashboardDigestSection
            {
                Order = order++,
                Key = "new-resources",
                Title = "New resources",
                Summary = $"{payload.NewResources.Count} new resource{(payload.NewResources.Count == 1 ? string.Empty : "s")} discovered.",
                Cards = payload.NewResources.Select(r => new DashboardDigestCard
                {
                    Title = r.Name,
                    Subtitle = r.ResourceType,
                    Detail = string.Empty,
                    PrimaryUrl = r.Url
                }).ToList()
            });
        }

        var activeDeferments = payload.ActiveDeferments.Select(d => new DashboardLiveDeferment
        {
            Scope = d.Label,
            Reason = d.Reason ?? "Active deferment.",
            ResumeLabel = "Resume time unknown"
        }).ToList();

        var isEmpty = sections.Count == 0;
        var runDate = latestRun.CompletedAt ?? latestRun.StartedAt;
        var caption = isEmpty
            ? $"The run on {runDate.ToLocalTime():MMM d} produced no new content."
            : $"From the {latestRun.RunType} run on {runDate.ToLocalTime():MMM d, yyyy}.";

        return new DashboardDigestSummary
        {
            IsEmpty = isEmpty,
            Heading = isEmpty ? "No new content" : "Latest digest",
            Caption = caption,
            EmptyHeadline = isEmpty ? "No new content" : null,
            EmptyMessage = isEmpty ? "Nothing new was found in the last ingestion run." : null,
            LiveDeferments = activeDeferments,
            Sections = sections
        };
    }

    private static IReadOnlyList<DashboardPendingActionItem> BuildPendingActions(
        int failedItemCount,
        int deferredItemCount,
        IngestionRun? latestRun)
    {
        var actions = new List<DashboardPendingActionItem>();
        var order = 0;

        if (failedItemCount > 0 && latestRun is not null)
        {
            actions.Add(new DashboardPendingActionItem
            {
                Order = order++,
                Key = "failed-items",
                Title = "Failed ingestion items",
                Severity = "error",
                Count = failedItemCount,
                Summary = $"{failedItemCount} item{(failedItemCount == 1 ? string.Empty : "s")} failed in the latest run.",
                DeepLink = $"/ingestion/runs/{latestRun.Id}",
                Actions =
                [
                    new DashboardActionLink
                    {
                        Label = "View run detail",
                        Href = $"/ingestion/runs/{latestRun.Id}",
                        IsPrimary = true
                    }
                ]
            });
        }

        if (deferredItemCount > 0 && latestRun is not null)
        {
            actions.Add(new DashboardPendingActionItem
            {
                Order = order++,
                Key = "deferred-items",
                Title = "Deferred ingestion items",
                Severity = "warning",
                Count = deferredItemCount,
                Summary = $"{deferredItemCount} item{(deferredItemCount == 1 ? string.Empty : "s")} deferred in the latest run.",
                DeepLink = $"/ingestion/runs/{latestRun.Id}",
                Actions =
                [
                    new DashboardActionLink
                    {
                        Label = "View deferments",
                        Href = $"/ingestion/runs/{latestRun.Id}",
                        IsPrimary = false
                    }
                ]
            });
        }

        return actions;
    }

    private static string BuildSummaryText(int channelCount, int videoCount, IngestionRun? latestRun)
    {
        var corpusPart = $"{videoCount:N0} video{(videoCount == 1 ? string.Empty : "s")} indexed across {channelCount:N0} channel{(channelCount == 1 ? string.Empty : "s")}.";
        if (latestRun is null)
        {
            return corpusPart;
        }

        var runDate = latestRun.CompletedAt ?? latestRun.StartedAt;
        return $"{corpusPart} Last run: {runDate.ToLocalTime():MMM d, yyyy}.";
    }
}
