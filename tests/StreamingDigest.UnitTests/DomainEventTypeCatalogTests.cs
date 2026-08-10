using StreamingDigest.Domain;

namespace StreamingDigest.UnitTests;

public sealed class DomainEventTypeCatalogTests
{
    [Fact]
    public void Catalog_contains_the_seeded_event_types()
    {
        var expected = new[]
        {
            DomainEventTypeCatalog.ScreenshotFileMissing,
            DomainEventTypeCatalog.TranscriptIngested,
            DomainEventTypeCatalog.TranscriptIngestFailed,
            DomainEventTypeCatalog.TranscriptCutoverCompleted,
            DomainEventTypeCatalog.TranscriptCutoverOverrideInert,
            DomainEventTypeCatalog.ScrapeExcluded,
            DomainEventTypeCatalog.ScrapeFailed,
            DomainEventTypeCatalog.RateLimitDefermentCreated,
            DomainEventTypeCatalog.RateLimitDefermentExpired,
            DomainEventTypeCatalog.RateLimitDefermentCleared,
            DomainEventTypeCatalog.ChannelDegradedEntered,
            DomainEventTypeCatalog.ChannelProbeSucceeded,
            DomainEventTypeCatalog.ChannelProbeFailed,
            DomainEventTypeCatalog.VideoUnavailableEntered,
            DomainEventTypeCatalog.OrphanedNoteSurfaced,
            DomainEventTypeCatalog.TempMediaOrphanCleanup,
            DomainEventTypeCatalog.EmbeddingReprocessQueued,
            DomainEventTypeCatalog.EmbeddingReprocessCompleted,
            DomainEventTypeCatalog.EmbeddingReprocessFailed,
            DomainEventTypeCatalog.NotificationDispatchOutcome,
            DomainEventTypeCatalog.DigestAssembled,
            DomainEventTypeCatalog.ModelReadinessDegraded
        };

        Assert.Equal(expected, DomainEventTypeCatalog.All);
    }

    [Fact]
    public void Catalog_accepts_known_types_and_rejects_unknown_values()
    {
        Assert.True(DomainEventTypeCatalog.IsDefined(DomainEventTypeCatalog.ScreenshotFileMissing));
        Assert.False(DomainEventTypeCatalog.IsDefined("unknown_event_type"));
        Assert.False(DomainEventTypeCatalog.IsDefined(string.Empty));
    }

    [Fact]
    public void Catalog_requires_defined_values_when_used_by_writers()
    {
        Assert.Equal(DomainEventTypeCatalog.ScrapeFailed, DomainEventTypeCatalog.RequireDefined(DomainEventTypeCatalog.ScrapeFailed));

        var ex = Assert.Throws<ArgumentException>(() => DomainEventTypeCatalog.RequireDefined("not_in_catalog"));
        Assert.Contains("not_in_catalog", ex.Message);
    }

    [Fact]
    public void Catalog_values_are_documented_in_the_data_model()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var dataModelPath = Path.Combine(repoRoot, "docs", "architecture", "DATA_MODEL.md");

        Assert.True(File.Exists(dataModelPath), $"Expected data model docs at {dataModelPath}");

        var dataModel = File.ReadAllText(dataModelPath);

        foreach (var eventType in DomainEventTypeCatalog.All)
        {
            Assert.Contains(eventType, dataModel);
        }
    }
}
