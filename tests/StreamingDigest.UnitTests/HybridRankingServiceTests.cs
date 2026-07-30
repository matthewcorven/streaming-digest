using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class HybridRankingServiceTests
{
    [Fact]
    public void Rank_orders_clusters_using_the_hybrid_formula_and_returns_the_expected_shape()
    {
        var service = new HybridRankingService();
        var clusters = new[]
        {
            new HybridClusterCandidate(
                Id: "cluster-1",
                Title: "Cluster one",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-1",
                        DocumentType: "title",
                        TextScore: 1.0,
                        VectorScore: 1.0,
                        MatchedFields: new[] { "title" },
                        Snippet: "Best match")
                },
                RecentOpenCount: 0,
                HasMatchingNote: false),
            new HybridClusterCandidate(
                Id: "cluster-2",
                Title: "Cluster two",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-2",
                        DocumentType: "note",
                        TextScore: 0.5,
                        VectorScore: 0.5,
                        MatchedFields: new[] { "body", "note" },
                        Snippet: "Related note")
                },
                RecentOpenCount: 0,
                HasMatchingNote: true),
            new HybridClusterCandidate(
                Id: "cluster-3",
                Title: "Cluster three",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-3",
                        DocumentType: "transcript_chunk",
                        TextScore: 0.0,
                        VectorScore: 0.0,
                        MatchedFields: new[] { "body" },
                        Snippet: "Low signal")
                },
                RecentOpenCount: 3,
                HasMatchingNote: false)
        };

        var results = service.Rank(clusters, new[] { 0.0, 0.5, 1.0 });

        Assert.Collection(results,
            first =>
            {
                Assert.Equal("cluster-1", first.ClusterId);
                Assert.Equal(0.925, first.Score);
                Assert.Equal(0.925, first.ScoreComponents.BaseScore);
                Assert.Equal(0.25, first.ScoreComponents.CoverageScore);
                Assert.Equal(0.0, first.ScoreComponents.NoteBoost);
                Assert.Equal(0.0, first.ScoreComponents.InteractionBoost);
                Assert.Equal(new[] { "title" }, first.MatchedFields);
                Assert.Contains(first.Snippets, snippet => snippet.Text == "Best match");
                Assert.Contains("Base score", first.Explanation);
            },
            second =>
            {
                Assert.Equal("cluster-2", second.ClusterId);
                Assert.Equal(0.555, second.Score);
                Assert.Equal(0.475, second.ScoreComponents.BaseScore);
                Assert.Equal(0.08, second.ScoreComponents.NoteBoost);
                Assert.Equal(0.0, second.ScoreComponents.InteractionBoost);
                Assert.Equal(new[] { "body", "note" }, second.MatchedFields);
            },
            third =>
            {
                Assert.Equal("cluster-3", third.ClusterId);
                Assert.Equal(0.055, third.Score);
                Assert.Equal(0.025, third.ScoreComponents.BaseScore);
                Assert.Equal(0.0, third.ScoreComponents.NoteBoost);
                Assert.Equal(0.03, third.ScoreComponents.InteractionBoost);
            });

        Assert.Equal(100.0, results[0].RelativeSimilarityPercent);
        Assert.Equal(50.0, results[1].RelativeSimilarityPercent);
        Assert.Equal(0.0, results[2].RelativeSimilarityPercent);
    }

    [Fact]
    public void Rank_uses_relative_similarity_label_and_tooltip_semantics()
    {
        var service = new HybridRankingService();
        var result = Assert.Single(service.Rank(new[]
        {
            new HybridClusterCandidate(
                Id: "cluster-1",
                Title: "Cluster one",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-1",
                        DocumentType: "title",
                        TextScore: 1.0,
                        VectorScore: 1.0,
                        MatchedFields: new[] { "title" },
                        Snippet: "Best match")
                },
                RecentOpenCount: 0,
                HasMatchingNote: false)
        }, new[] { 1.0 }));

        Assert.Equal("Relative similarity", result.RelativeSimilarityLabel);
        Assert.Equal("Relative similarity is a normalized vector rank score within the current result set and is relative to the query, model, and result set rather than a confidence score.", result.RelativeSimilarityTooltip);
        Assert.Equal(100.0, result.RelativeSimilarityPercent);
    }

    [Fact]
    public void Rank_returns_empty_results_for_empty_input()
    {
        var service = new HybridRankingService();

        Assert.Empty(service.Rank(Array.Empty<HybridClusterCandidate>()));
    }

    [Fact]
    public void Rank_rejects_non_finite_and_negative_scores()
    {
        var service = new HybridRankingService();
        var clusters = new[]
        {
            new HybridClusterCandidate(
                Id: "cluster-1",
                Title: "Cluster one",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-1",
                        DocumentType: "title",
                        TextScore: double.NaN,
                        VectorScore: 1.0)
                },
                RecentOpenCount: 0,
                HasMatchingNote: false)
        };

        Assert.Throws<ArgumentException>(() => service.Rank(clusters));
    }

    [Fact]
    public void Rank_rejects_negative_recent_open_count()
    {
        var service = new HybridRankingService();
        var clusters = new[]
        {
            new HybridClusterCandidate(
                Id: "cluster-1",
                Title: "Cluster one",
                Documents: new[]
                {
                    new HybridDocumentCandidate(
                        Id: "doc-1",
                        DocumentType: "title",
                        TextScore: 1.0,
                        VectorScore: 1.0)
                },
                RecentOpenCount: -1,
                HasMatchingNote: false)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Rank(clusters));
    }
}
