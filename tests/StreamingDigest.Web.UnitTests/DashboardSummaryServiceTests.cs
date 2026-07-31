using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class DashboardSummaryServiceTests
{
    private readonly DashboardSummaryService _service = new();

    [Fact]
    public void Default_summary_orders_digest_sections_by_required_priority()
    {
        var summary = _service.GetSummary();

        var orderedTitles = summary.Digest.Sections
            .OrderBy(section => section.Order)
            .Select(section => section.Title)
            .ToArray();

        Assert.Equal(
            [
                "New videos ingested",
                "New repositories found",
                "New websites and resources found",
                "Similar to recent searches",
                "Failed and skipped items"
            ],
            orderedTitles);
    }

    [Fact]
    public void Default_summary_high_signal_cards_include_relative_similarity_and_artifact_links()
    {
        var summary = _service.GetSummary();

        var section = Assert.Single(summary.Digest.Sections, section => section.Key == "recent-search-matches");
        Assert.All(section.Cards, card =>
        {
            Assert.True(card.RelativeSimilarityPercent.HasValue);
            Assert.False(string.IsNullOrWhiteSpace(card.TimestampUrl));
            Assert.False(string.IsNullOrWhiteSpace(card.RepositoryUrl));
            Assert.False(string.IsNullOrWhiteSpace(card.WebsiteUrl));
        });
    }

    [Fact]
    public void Pending_action_fixture_exposes_retry_approve_and_test_actions_without_log_links()
    {
        var summary = _service.GetSummary("pending-actions");

        var actionLabels = summary.PendingActions
            .SelectMany(item => item.Actions)
            .Select(action => action.Label)
            .ToArray();

        Assert.Contains("Approve", actionLabels);
        Assert.Contains("Retry", actionLabels);
        Assert.Contains("Test", actionLabels);
        Assert.DoesNotContain(summary.PendingActions, item => string.Equals(item.DeepLink, "/logs", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, null, false, "/channels")]
    [InlineData(true, "/search", false, "/search")]
    [InlineData(true, "/dashboard", false, "/dashboard")]
    [InlineData(true, null, true, "/dashboard")]
    [InlineData(true, null, false, "/ingestion")]
    public void Resolve_post_login_route_follows_onboarding_then_last_mode_then_dashboard_then_ingestion(
        bool isCoreSetupComplete,
        string? lastSelectedMode,
        bool hasCompletedRun,
        string expectedRoute)
    {
        var route = _service.ResolvePostLoginRoute(isCoreSetupComplete, lastSelectedMode, hasCompletedRun);

        Assert.Equal(expectedRoute, route);
    }
}
