using StreamingDigest.Web.Models;
using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class CorpusReadinessEvaluatorTests
{
    [Fact]
    public void No_runs_state_only_points_people_to_adding_channels()
    {
        var state = CorpusReadinessEvaluator.NoRuns();

        Assert.False(state.HasAnyRuns);
        Assert.False(state.HasSearchableCorpus);
        Assert.Contains("Add", state.WaitingHeadline, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("run ingestion", state.WaitingMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backfill", state.WaitingMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, state.BackfillGuidance);
    }

    [Fact]
    public void Zero_video_runs_state_keeps_backfill_guidance_hidden()
    {
        var state = CorpusReadinessEvaluator.Evaluate(
        [
            CreateRun(processedVideos: 0),
            CreateRun(processedVideos: 0)
        ]);

        Assert.True(state.HasAnyRuns);
        Assert.False(state.HasSearchableCorpus);
        Assert.True(state.LatestRunFoundZeroVideos);
        Assert.DoesNotContain("run ingestion", state.WaitingMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backfill", state.WaitingMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, state.BackfillGuidance);
    }

    [Fact]
    public void Processed_run_unlocks_searchable_corpus()
    {
        var state = CorpusReadinessEvaluator.Evaluate(
        [
            CreateRun(processedVideos: 0),
            CreateRun(processedVideos: 2)
        ]);

        Assert.True(state.HasAnyRuns);
        Assert.True(state.HasSearchableCorpus);
        Assert.Equal(string.Empty, state.WaitingHeadline);
        Assert.Equal(string.Empty, state.WaitingMessage);
    }

    private static IngestionRunDetailViewModel CreateRun(int processedVideos)
        => new()
        {
            FrozenOutcome = new IngestionRunOutcomeViewModel
            {
                ProcessedVideos = processedVideos
            }
        };
}