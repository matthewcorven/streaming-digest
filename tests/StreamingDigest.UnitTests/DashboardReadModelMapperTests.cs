using StreamingDigest.Api.Endpoints;
using StreamingDigest.Domain;
using StreamingDigest.Web.Models;

namespace StreamingDigest.UnitTests;

public sealed class DashboardReadModelMapperTests
{
    // ── no-corpus path ─────────────────────────────────────────────────────────

    [Fact]
    public void Empty_corpus_produces_no_searchable_corpus_state()
    {
        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 0, videoCount: 0,
            latestRun: null, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.False(summary.Corpus.HasSearchableCorpus);
        Assert.False(summary.Corpus.HasCompletedRun);
        Assert.Empty(summary.PendingActions);
        Assert.True(summary.Digest.IsEmpty);
        Assert.Equal(string.Empty, summary.Summary);
    }

    [Fact]
    public void No_channels_produces_add_channel_waiting_headline()
    {
        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 0, videoCount: 0,
            latestRun: null, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.Contains("Add", summary.Corpus.WaitingHeadline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("channel", summary.Corpus.WaitingMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Channels_but_no_videos_produces_run_ingestion_guidance()
    {
        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 3, videoCount: 0,
            latestRun: null, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.False(summary.Corpus.HasSearchableCorpus);
        Assert.Contains("ingestion", summary.Corpus.WaitingMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── corpus path ────────────────────────────────────────────────────────────

    [Fact]
    public void Corpus_present_sets_has_searchable_corpus_and_populates_summary_text()
    {
        var run = CreateCompletedRun(newVideos: 10, processed: 10, failed: 0);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 2, videoCount: 100,
            latestRun: run, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.True(summary.Corpus.HasSearchableCorpus);
        Assert.True(summary.Corpus.HasCompletedRun);
        Assert.Contains("100", summary.Summary);
        Assert.Contains("2", summary.Summary);
    }

    // ── digest payload ─────────────────────────────────────────────────────────

    [Fact]
    public void New_videos_in_digest_appear_as_a_digest_section()
    {
        var run = CreateCompletedRun(newVideos: 2, processed: 2, failed: 0);
        var payload = new DigestPayload
        {
            NewVideos =
            [
                new DigestItem { Id = "v1", Label = "Video one" },
                new DigestItem { Id = "v2", Label = "Video two" }
            ]
        };
        var digest = CreateDigestWithPayload(run.Id, payload);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 2,
            latestRun: run, latestDigest: digest,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.False(summary.Digest.IsEmpty);
        var section = Assert.Single(summary.Digest.Sections, s => s.Key == "new-videos");
        Assert.Equal(2, section.Cards.Count);
        Assert.Contains(section.Cards, c => c.Title == "Video one");
        Assert.Contains(section.Cards, c => c.Title == "Video two");
    }

    [Fact]
    public void High_signal_matches_appear_as_a_digest_section_with_similarity()
    {
        var run = CreateCompletedRun(newVideos: 0, processed: 0, failed: 0);
        var payload = new DigestPayload
        {
            HighSignalMatches =
            [
                new HighSignalMatch { Id = "m1", Label = "Match one", SimilarityPercent = 92 }
            ]
        };
        var digest = CreateDigestWithPayload(run.Id, payload);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: digest,
            failedItemCount: 0, deferredItemCount: 0);

        var section = Assert.Single(summary.Digest.Sections, s => s.Key == "high-signal");
        var card = Assert.Single(section.Cards);
        Assert.Equal(92, card.RelativeSimilarityPercent);
    }

    [Fact]
    public void Active_deferments_in_payload_surface_as_live_deferments()
    {
        var run = CreateCompletedRun(newVideos: 0, processed: 0, failed: 0);
        var payload = new DigestPayload
        {
            ActiveDeferments =
            [
                new ActiveDeferment { Id = "d1", Label = "GitHub API", Reason = "Rate limited." }
            ]
        };
        var digest = CreateDigestWithPayload(run.Id, payload);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: digest,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.Single(summary.Digest.LiveDeferments);
        Assert.Equal("GitHub API", summary.Digest.LiveDeferments[0].Scope);
        Assert.Equal("Rate limited.", summary.Digest.LiveDeferments[0].Reason);
    }

    [Fact]
    public void Empty_digest_payload_produces_empty_digest_summary_with_caption()
    {
        var run = CreateCompletedRun(newVideos: 0, processed: 0, failed: 0);
        var payload = new DigestPayload();
        var digest = CreateDigestWithPayload(run.Id, payload);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: digest,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.True(summary.Digest.IsEmpty);
        Assert.NotNull(summary.Digest.EmptyMessage);
    }

    // ── pending-actions path ────────────────────────────────────────────────────

    [Fact]
    public void Failed_items_produce_an_error_severity_pending_action()
    {
        var run = CreateCompletedRun(newVideos: 5, processed: 4, failed: 1);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: null,
            failedItemCount: 3, deferredItemCount: 0);

        var action = Assert.Single(summary.PendingActions, a => a.Key == "failed-items");
        Assert.Equal("error", action.Severity);
        Assert.Equal(3, action.Count);
        Assert.NotNull(action.DeepLink);
        Assert.Contains(run.Id.ToString(), action.DeepLink);
    }

    [Fact]
    public void Deferred_items_produce_a_warning_severity_pending_action()
    {
        var run = CreateCompletedRun(newVideos: 5, processed: 5, failed: 0);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 2);

        var action = Assert.Single(summary.PendingActions, a => a.Key == "deferred-items");
        Assert.Equal("warning", action.Severity);
        Assert.Equal(2, action.Count);
    }

    [Fact]
    public void No_failed_or_deferred_items_produces_empty_pending_actions()
    {
        var run = CreateCompletedRun(newVideos: 5, processed: 5, failed: 0);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 1, videoCount: 5,
            latestRun: run, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.Empty(summary.PendingActions);
    }

    // ── search launchpad ────────────────────────────────────────────────────────

    [Fact]
    public void Search_launchpad_references_corpus_size_when_corpus_present()
    {
        var run = CreateCompletedRun(newVideos: 5, processed: 5, failed: 0);

        var summary = DashboardReadModelMapper.MapToSummary(
            channelCount: 2, videoCount: 50,
            latestRun: run, latestDigest: null,
            failedItemCount: 0, deferredItemCount: 0);

        Assert.Contains("50", summary.SearchLaunchpad.Prompt);
        Assert.Contains("2", summary.SearchLaunchpad.Prompt);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static IngestionRun CreateCompletedRun(int newVideos, int processed, int failed)
        => new()
        {
            Id = Guid.NewGuid(),
            RunType = "standard",
            TriggeredBy = "test",
            Status = "completed",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            NewVideosFound = newVideos,
            VideosIngested = processed,
            VideosFailed = failed,
            ChannelsChecked = 1,
            RepositoriesFound = 0
        };

    private static Digest CreateDigestWithPayload(Guid ingestionRunId, DigestPayload payload)
        => new(ingestionRunId, "standard")
        {
            Id = Guid.NewGuid(),
            PayloadJson = DigestPayloadSerializer.Serialize(payload)
        };
}
