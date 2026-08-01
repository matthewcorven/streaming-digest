using StreamingDigest.Web.Models;
using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class CorpusReadinessEvaluatorTests
{
    [Fact]
    public void No_runs_returns_pre_corpus_waiting_state()
    {
        var state = CorpusReadinessEvaluator.NoRuns();

        Assert.False(state.HasAnyRuns);
        Assert.False(state.HasSearchableCorpus);
        Assert.Equal("Nothing to search yet", state.WaitingHeadline);
        Assert.Contains("pre-corpus waiting state", state.WaitingMessage, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Zero_video_runs_keep_corpus_in_warming_state_with_backfill_guidance()
    {
        var state = CorpusReadinessEvaluator.Evaluate(
        [
            CreateRun(processedVideos: 0),
            CreateRun(processedVideos: 0)
        ]);

        Assert.True(state.HasAnyRuns);
        Assert.False(state.HasSearchableCorpus);
        Assert.True(state.LatestRunFoundZeroVideos);
        Assert.Equal("Corpus still warming up", state.WaitingHeadline);
        Assert.Contains("zero ingested videos", state.BackfillGuidance, StringComparison.OrdinalIgnoreCase);
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