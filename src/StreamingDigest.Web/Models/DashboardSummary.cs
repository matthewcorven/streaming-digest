namespace StreamingDigest.Web.Models;

public sealed class DashboardSummary
{
    public string Title { get; init; } = "Dashboard";
    public string Summary { get; init; } = string.Empty;
    public DashboardDigestSummary Digest { get; init; } = new();
    public DashboardSearchLaunchpad SearchLaunchpad { get; init; } = new();
    public IReadOnlyList<DashboardPendingActionItem> PendingActions { get; init; } = [];
    public DashboardCorpusState Corpus { get; init; } = new();
}

public sealed class DashboardDigestSummary
{
    public bool IsEmpty { get; init; }
    public string Heading { get; init; } = "Daily digest";
    public string Caption { get; init; } = string.Empty;
    public string? EmptyHeadline { get; init; }
    public string? EmptyMessage { get; init; }
    public IReadOnlyList<DashboardLiveDeferment> LiveDeferments { get; init; } = [];
    public IReadOnlyList<DashboardDigestSection> Sections { get; init; } = [];
}

public sealed class DashboardLiveDeferment
{
    public string Scope { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string ResumeLabel { get; init; } = string.Empty;
}

public sealed class DashboardDigestSection
{
    public int Order { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DashboardDigestCard> Cards { get; init; } = [];
}

public sealed class DashboardDigestCard
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? Badge { get; init; }
    public string BadgeTone { get; init; } = "neutral";
    public double? RelativeSimilarityPercent { get; init; }
    public string? MatchingQuery { get; init; }
    public string? TimestampLabel { get; init; }
    public string? TimestampUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? PrimaryUrl { get; init; }
}

public sealed class DashboardSearchLaunchpad
{
    public string Heading { get; init; } = "Search launchpad";
    public string Prompt { get; init; } = string.Empty;
    public string QueryPlaceholder { get; init; } = "Search your videos…";
    public IReadOnlyList<string> RecentSearches { get; init; } = [];
}

public sealed class DashboardPendingActionItem
{
    public int Order { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Severity { get; init; } = "info";
    public int Count { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? DeepLink { get; init; }
    public IReadOnlyList<DashboardActionLink> Actions { get; init; } = [];
}

public sealed class DashboardActionLink
{
    public string Label { get; init; } = string.Empty;
    public string Href { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
}

public sealed class DashboardCorpusState
{
    public bool HasSearchableCorpus { get; init; }
    public bool HasCompletedRun { get; init; }
    public bool LatestCompletedRunFoundZeroVideos { get; init; }
    public string WaitingHeadline { get; init; } = "Nothing to search yet";
    public string WaitingMessage { get; init; } = string.Empty;
    public string RunNowLabel { get; init; } = "Run ingestion now";
    public string BackfillGuidance { get; init; } = string.Empty;
}
