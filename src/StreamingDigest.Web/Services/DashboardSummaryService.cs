using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

public sealed class DashboardSummaryService
{
    public DashboardSummary GetSummary(string? fixture = null)
        => NormalizeFixtureKey(fixture) switch
        {
            "empty" => CreateEmptySummary(),
            "pending-actions" => CreatePendingActionFixture(),
            _ => CreateDefaultSummary()
        };

    public string ResolvePostLoginRoute(bool isCoreSetupComplete, string? lastSelectedMode, bool hasCompletedRun)
    {
        if (!isCoreSetupComplete)
        {
            return "/channels";
        }

        var normalizedMode = NormalizeModeRoute(lastSelectedMode);
        if (normalizedMode is not null)
        {
            return normalizedMode;
        }

        return hasCompletedRun ? "/dashboard" : "/ingestion";
    }

    public string? NormalizeModeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        return route.Trim().ToLowerInvariant() switch
        {
            "/" or "/dashboard" => "/dashboard",
            "/search" => "/search",
            _ => null
        };
    }

    private static string NormalizeFixtureKey(string? fixture)
        => string.IsNullOrWhiteSpace(fixture) ? "default" : fixture.Trim().ToLowerInvariant();

    private static DashboardSummary CreateDefaultSummary()
    {
        return new DashboardSummary
        {
            Summary = "Daily digest first, search launchpad second, and the pending inbox third.",
            Digest = new DashboardDigestSummary
            {
                Caption = "Stored run artifact with live deferments re-derived at render time.",
                LiveDeferments =
                [
                    new DashboardLiveDeferment
                    {
                        Scope = "GitHub API",
                        Reason = "Repository enrichment is paused while the host cools down.",
                        ResumeLabel = "Resumes 7:15 AM"
                    }
                ],
                Sections =
                [
                    new DashboardDigestSection
                    {
                        Order = 1,
                        Key = "new-videos",
                        Title = "New videos ingested",
                        Summary = "2 new videos landed in the latest scheduled run.",
                        Cards =
                        [
                            new DashboardDigestCard
                            {
                                Title = "Project ideas from GitHub trend shifts",
                                Subtitle = "Tonbis AI Garage · 24m",
                                Detail = "Fresh ingestion with chapter screenshots and timestamps ready.",
                                Badge = "New",
                                BadgeTone = "success",
                                TimestampLabel = "Open 12:34",
                                TimestampUrl = "https://youtube.com/watch?v=video-one&t=754s",
                                PrimaryUrl = "https://youtube.com/watch?v=video-one"
                            },
                            new DashboardDigestCard
                            {
                                Title = "Building local-first ranking workflows",
                                Subtitle = "Practical AI Weekly · 18m",
                                Detail = "Transcript, screenshots, and cluster links are fully indexed.",
                                Badge = "New",
                                BadgeTone = "success",
                                TimestampLabel = "Open 03:12",
                                TimestampUrl = "https://youtube.com/watch?v=video-two&t=192s",
                                PrimaryUrl = "https://youtube.com/watch?v=video-two"
                            }
                        ]
                    },
                    new DashboardDigestSection
                    {
                        Order = 2,
                        Key = "new-repositories",
                        Title = "New repositories found",
                        Summary = "2 repositories were extracted from new video descriptions.",
                        Cards =
                        [
                            new DashboardDigestCard
                            {
                                Title = "rank-lab/hybrid-cluster-explorer",
                                Subtitle = "GitHub repository",
                                Detail = "README and license were ingested for downstream search matches.",
                                Badge = "Repo",
                                BadgeTone = "info",
                                RepositoryUrl = "https://github.com/rank-lab/hybrid-cluster-explorer",
                                PrimaryUrl = "https://github.com/rank-lab/hybrid-cluster-explorer"
                            },
                            new DashboardDigestCard
                            {
                                Title = "mcorven/streaming-digest-samples",
                                Subtitle = "GitHub repository",
                                Detail = "DeepWiki is reachable and the README now contributes search documents.",
                                Badge = "Repo",
                                BadgeTone = "info",
                                RepositoryUrl = "https://github.com/mcorven/streaming-digest-samples",
                                PrimaryUrl = "https://github.com/mcorven/streaming-digest-samples"
                            }
                        ]
                    },
                    new DashboardDigestSection
                    {
                        Order = 3,
                        Key = "new-websites",
                        Title = "New websites and resources found",
                        Summary = "2 linked resources were scraped from the same run.",
                        Cards =
                        [
                            new DashboardDigestCard
                            {
                                Title = "Relative ranking calibration notes",
                                Subtitle = "Website resource",
                                Detail = "First-page scrape captured visible text and metadata.",
                                Badge = "Website",
                                BadgeTone = "accent",
                                WebsiteUrl = "https://example.com/ranking-calibration",
                                PrimaryUrl = "https://example.com/ranking-calibration"
                            },
                            new DashboardDigestCard
                            {
                                Title = "Vector recall tuning worksheet",
                                Subtitle = "Website resource",
                                Detail = "New page content is ready for search expansion and note attachment.",
                                Badge = "Website",
                                BadgeTone = "accent",
                                WebsiteUrl = "https://example.com/vector-recall",
                                PrimaryUrl = "https://example.com/vector-recall"
                            }
                        ]
                    },
                    new DashboardDigestSection
                    {
                        Order = 4,
                        Key = "recent-search-matches",
                        Title = "Similar to recent searches",
                        Summary = "High-signal matches use relative-similarity percentages from the active search UI ranking.",
                        Cards =
                        [
                            new DashboardDigestCard
                            {
                                Title = "Hybrid rank explorer timestamp",
                                Subtitle = "Tonbis AI Garage · segment match",
                                Detail = "Matches the recent search 'github idea search' and links to the best timestamp plus related artifacts.",
                                Badge = "High signal",
                                BadgeTone = "warning",
                                RelativeSimilarityPercent = 92,
                                MatchingQuery = "github idea search",
                                TimestampLabel = "Open 12:34",
                                TimestampUrl = "https://youtube.com/watch?v=video-one&t=754s",
                                RepositoryUrl = "https://github.com/rank-lab/hybrid-cluster-explorer",
                                WebsiteUrl = "https://example.com/ranking-calibration",
                                PrimaryUrl = "https://youtube.com/watch?v=video-one&t=754s"
                            },
                            new DashboardDigestCard
                            {
                                Title = "Postgres vector backfill recipe",
                                Subtitle = "Website + repository match",
                                Detail = "Matches the recent search 'postgres vector' with direct repository and website follow-through.",
                                Badge = "High signal",
                                BadgeTone = "warning",
                                RelativeSimilarityPercent = 78,
                                MatchingQuery = "postgres vector",
                                TimestampLabel = "Open 03:12",
                                TimestampUrl = "https://youtube.com/watch?v=video-two&t=192s",
                                RepositoryUrl = "https://github.com/mcorven/streaming-digest-samples",
                                WebsiteUrl = "https://example.com/vector-recall",
                                PrimaryUrl = "https://youtube.com/watch?v=video-two&t=192s"
                            }
                        ]
                    },
                    new DashboardDigestSection
                    {
                        Order = 5,
                        Key = "failed-skipped",
                        Title = "Failed and skipped items",
                        Summary = "Items that need manual follow-up stay visible at the end of the digest.",
                        Cards =
                        [
                            new DashboardDigestCard
                            {
                                Title = "Embedding retry pending",
                                Subtitle = "Video title two · embeddings",
                                Detail = "The embedding stage timed out and is ready for retry from the inbox.",
                                Badge = "Failed",
                                BadgeTone = "danger",
                                PrimaryUrl = "/ingestion"
                            },
                            new DashboardDigestCard
                            {
                                Title = "Skipped sponsor-only resource",
                                Subtitle = "Description link classification",
                                Detail = "Non-essential sponsor content was skipped without blocking ingestion.",
                                Badge = "Skipped",
                                BadgeTone = "neutral",
                                PrimaryUrl = "/ingestion"
                            }
                        ]
                    }
                ]
            },
            SearchLaunchpad = new DashboardSearchLaunchpad
            {
                Prompt = "Launch directly into search with your most recent interests and a one-click path back to the corpus.",
                RecentSearches = ["github idea search", "postgres vector", "llm classification"]
            },
            PendingActions =
            [
                new DashboardPendingActionItem
                {
                    Order = 1,
                    Key = "pending-approvals",
                    Title = "Pending approvals",
                    Severity = "warning",
                    Count = 2,
                    Summary = "Two regenerated segment batches still need human approval before they become active.",
                    DeepLink = "/ingestion",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Approve", Href = "/ingestion", IsPrimary = true }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 2,
                    Key = "failed-ingestion",
                    Title = "Failed ingestion",
                    Severity = "danger",
                    Count = 1,
                    Summary = "A failed embeddings stage can be retried directly from the dashboard.",
                    DeepLink = "/ingestion",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Retry", Href = "/ingestion", IsPrimary = true }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 3,
                    Key = "degraded-channels",
                    Title = "Degraded channels",
                    Severity = "warning",
                    Count = 1,
                    Summary = "One permanently degraded channel needs an explicit exit: pause probing or delete it.",
                    DeepLink = "/channels",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Pause", Href = "/channels", IsPrimary = true },
                        new DashboardActionLink { Label = "Delete", Href = "/channels" }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 4,
                    Key = "deferred-rate-limits",
                    Title = "Deferred rate limits",
                    Severity = "info",
                    Count = 1,
                    Summary = "A GitHub deferment is still active and will clear when the retry window opens.",
                    DeepLink = "/ingestion"
                },
                new DashboardPendingActionItem
                {
                    Order = 5,
                    Key = "stale-embeddings",
                    Title = "Stale embeddings",
                    Severity = "warning",
                    Count = 9,
                    Summary = "Derived data is waiting for reprocessing after the active model update.",
                    DeepLink = "/admin/upgrade-maintenance"
                },
                new DashboardPendingActionItem
                {
                    Order = 6,
                    Key = "model-service-warnings",
                    Title = "Model and service warnings",
                    Severity = "warning",
                    Count = 2,
                    Summary = "Matrix and embedding verification actions are available without opening logs first.",
                    DeepLink = "/settings",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Test", Href = "/settings", IsPrimary = true }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 7,
                    Key = "new-digest-items",
                    Title = "New digest items",
                    Severity = "info",
                    Count = 4,
                    Summary = "Fresh digest cards are waiting in the run summary.",
                    DeepLink = "/dashboard"
                },
                new DashboardPendingActionItem
                {
                    Order = 8,
                    Key = "recent-search-matches",
                    Title = "Recent-search matches",
                    Severity = "info",
                    Count = 2,
                    Summary = "Two high-signal items line up with your latest saved searches.",
                    DeepLink = "/search"
                },
                new DashboardPendingActionItem
                {
                    Order = 9,
                    Key = "storage-retention",
                    Title = "Storage and retention warnings",
                    Severity = "warning",
                    Count = 1,
                    Summary = "Observability retention still needs confirmation before the next deployment cycle.",
                    DeepLink = "/settings"
                }
            ],
            Corpus = new DashboardCorpusState
            {
                HasSearchableCorpus = true,
                HasCompletedRun = true,
                LatestCompletedRunFoundZeroVideos = false,
                WaitingMessage = "Run ingestion now to create your first searchable corpus.",
                BackfillGuidance = "If the first completed run finds zero videos, widen the backfill window or add another channel before returning to search."
            }
        };
    }

    private static DashboardSummary CreatePendingActionFixture()
    {
        return new DashboardSummary
        {
            Summary = "Pending-action fixture focused on direct retry, approve, and test affordances.",
            Digest = CreateDefaultSummary().Digest,
            SearchLaunchpad = CreateDefaultSummary().SearchLaunchpad,
            PendingActions =
            [
                new DashboardPendingActionItem
                {
                    Order = 1,
                    Key = "pending-approvals",
                    Title = "Pending approvals",
                    Severity = "warning",
                    Count = 3,
                    Summary = "Three segment cutover batches are waiting for approval.",
                    DeepLink = "/ingestion",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Approve", Href = "/ingestion", IsPrimary = true }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 2,
                    Key = "failed-ingestion",
                    Title = "Failed ingestion",
                    Severity = "danger",
                    Count = 2,
                    Summary = "Two failed ingestion items are retryable from the dashboard.",
                    DeepLink = "/ingestion",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Retry", Href = "/ingestion", IsPrimary = true }
                    ]
                },
                new DashboardPendingActionItem
                {
                    Order = 6,
                    Key = "model-service-warnings",
                    Title = "Model and service warnings",
                    Severity = "warning",
                    Count = 2,
                    Summary = "Embedding and Matrix checks are exposed as direct test actions.",
                    DeepLink = "/settings",
                    Actions =
                    [
                        new DashboardActionLink { Label = "Test", Href = "/settings", IsPrimary = true }
                    ]
                }
            ],
            Corpus = CreateDefaultSummary().Corpus
        };
    }

    private static DashboardSummary CreateEmptySummary()
    {
        return new DashboardSummary
        {
            Summary = "Add a channel to begin building the corpus.",
            Digest = new DashboardDigestSummary
            {
                IsEmpty = true,
                Caption = "Add a channel to unlock the dashboard experience.",
                EmptyHeadline = "Your daily digest will appear here",
                EmptyMessage = "Add your first channel to start building your knowledge base."
            },
            SearchLaunchpad = new DashboardSearchLaunchpad
            {
                Prompt = "Add a channel to unlock search and recent searches."
            },
            PendingActions = [],
            Corpus = new DashboardCorpusState
            {
                HasSearchableCorpus = false,
                HasCompletedRun = false,
                LatestCompletedRunFoundZeroVideos = false,
                WaitingHeadline = "Start by adding a channel",
                WaitingMessage = "Add a channel to begin building the corpus. Search, the dashboard, and run history will appear after the first videos become available.",
                BackfillGuidance = string.Empty
            }
        };
    }
}
