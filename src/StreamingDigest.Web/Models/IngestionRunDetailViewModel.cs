namespace StreamingDigest.Web.Models;

public sealed class IngestionRunDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool HasActiveDeferments => Deferments.Any(d => d.IsActive);
    public string? DefermentBanner { get; set; }

    public IngestionRunOutcomeViewModel FrozenOutcome { get; set; } = new();
    public IngestionRunOutcomeViewModel LiveRollup { get; set; } = new();
    public List<IngestionRunStageViewModel> Stages { get; set; } = [];
    public List<IngestionRunItemViewModel> Items { get; set; } = [];
    public List<IngestionRunDefermentViewModel> Deferments { get; set; } = [];
    public List<IngestionRunLinkViewModel> Links { get; set; } = [];
}

public sealed class IngestionRunOutcomeViewModel
{
    public string Heading { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public int Channels { get; set; }
    public int FoundVideos { get; set; }
    public int ProcessedVideos { get; set; }
    public int FailedVideos { get; set; }
    public int Repositories { get; set; }
    public int Websites { get; set; }
}

public sealed class IngestionRunStageViewModel
{
    public string Name { get; set; } = string.Empty;
    public int Completed { get; set; }
    public int Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class IngestionRunItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool CanRetry { get; set; }
    public string? FailureSummary { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string TranscriptStatus { get; set; } = string.Empty;
    public string ScreenshotStatus { get; set; } = string.Empty;
    public string EmbeddingStatus { get; set; } = string.Empty;
    public int LinkCount { get; set; }
    public int RepositoryCount { get; set; }
    public int WebsiteCount { get; set; }
    public List<IngestionRunRetryEventViewModel> RetryHistory { get; set; } = [];
}

public sealed class IngestionRunRetryEventViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class IngestionRunDefermentViewModel
{
    public string Scope { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ResumeLabel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class IngestionRunLinkViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class IngestionRunFixtureSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
}
