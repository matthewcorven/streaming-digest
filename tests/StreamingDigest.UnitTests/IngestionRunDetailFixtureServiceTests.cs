using StreamingDigest.Web.Services;

namespace StreamingDigest.UnitTests;

public class IngestionRunDetailFixtureServiceTests
{
    private readonly IIngestionRunDetailFixtureService _service = new IngestionRunDetailFixtureService();

    [Fact]
    public void GetFixtures_ReturnsFixtureSummariesForAllScenarios()
    {
        var fixtures = _service.GetFixtures();

        Assert.Collection(
            fixtures,
            fixture => Assert.Equal("fixture-regular", fixture.Id),
            fixture => Assert.Equal("fixture-deferments", fixture.Id),
            fixture => Assert.Equal("fixture-completed", fixture.Id));
    }

    [Fact]
    public void GetRun_RegularFixture_ExposesStageTimelineItemStatusAndRetryHistory()
    {
        var run = _service.GetRun("fixture-regular");

        Assert.Equal(5, run.Stages.Count);
        Assert.Contains(run.Stages, stage => stage.Name == "embeddings" && stage.Status == "warning");
        Assert.Contains(run.Items, item => item.Id == "item-two" && item.CanRetry && item.RetryHistory.Count > 0);
        Assert.Contains(run.Items, item => item.Id == "item-three" && item.Status == "deferred");
    }

    [Fact]
    public void GetRun_DefermentFixture_RendersProminentActiveDeferments()
    {
        var run = _service.GetRun("fixture-deferments");

        Assert.True(run.HasActiveDeferments);
        Assert.Contains(run.Deferments, deferment => deferment.IsActive && deferment.Scope == "GitHub API");
        Assert.Equal("Repository and website processing is paused while the GitHub API cools down.", run.DefermentBanner);
    }

    [Fact]
    public void GetRun_CompletedFixture_ShowsFrozenOutcomeAndLiveRollupSideBySide()
    {
        var run = _service.GetRun("fixture-completed");

        Assert.True(run.IsCompleted);
        Assert.NotEqual(run.FrozenOutcome.ProcessedVideos, run.LiveRollup.ProcessedVideos);
        Assert.Equal(4, run.FrozenOutcome.ProcessedVideos);
        Assert.Equal(5, run.LiveRollup.ProcessedVideos);
        Assert.Contains(run.Items, item => item.Id == "completed-item-two" && item.Status == "processed" && item.RetryHistory.Count > 0);
    }
}
