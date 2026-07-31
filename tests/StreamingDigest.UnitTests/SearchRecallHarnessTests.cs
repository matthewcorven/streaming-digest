using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchRecallHarnessTests
{
    [Fact]
    public void Recall_fixture_dataset_meets_the_top_3_target_on_the_representative_corpus()
    {
        var dataset = RecallHarnessSupport.LoadDataset();
        Assert.True(dataset.Queries.Count >= 20, "The recall golden dataset must contain at least 20 vague queries.");

        var report = RecallHarnessSupport.BuildPassingReport();

        var failures = report.Queries
            .Where(query => !query.PassedTop3Recall)
            .Select(query => $"{query.Id} ranked {query.ExpectedClusterRank?.ToString() ?? "miss"}; top3={string.Join(", ", query.TopThree.Select(result => result.ClusterId))}")
            .ToArray();

        Assert.Equal(500, report.CorpusVideoCount);
        Assert.Equal(report.QueryCount, report.PassedQueryCount);
        Assert.True(failures.Length == 0, $"Expected 100% top-3 recall. Failures: {string.Join(" | ", failures)}");
    }

    [Fact]
    public void Recall_harness_detects_a_regressed_cluster_when_semantic_signal_drops_out_of_the_top_3()
    {
        var regressedCorpus = SearchRecallRepresentativeCorpusFactory.CreateRepresentativeCorpus()
            .Select(cluster =>
            {
                if (string.Equals(cluster.Id, "cluster-transcript-notes", StringComparison.OrdinalIgnoreCase))
                {
                    return cluster with
                    {
                        HasMatchingNote = false,
                        Documents = cluster.Documents
                            .Select(document => document with
                            {
                                TextScore = Math.Max(0.08, document.TextScore - 0.32),
                                VectorScore = Math.Max(0.08, document.VectorScore - 0.3)
                            })
                            .ToArray()
                    };
                }

                if (cluster.Id.StartsWith("cluster-transcript-notes-distractor-", StringComparison.OrdinalIgnoreCase))
                {
                    return cluster with
                    {
                        HasMatchingNote = true,
                        RecentOpenCount = cluster.RecentOpenCount + 3,
                        Documents = cluster.Documents
                            .Select(document => document with
                            {
                                TextScore = Math.Min(0.92, document.TextScore + 0.18),
                                VectorScore = Math.Min(0.94, document.VectorScore + 0.2)
                            })
                            .ToArray()
                    };
                }

                return cluster;
            })
            .ToArray();

        var report = RecallHarnessSupport.BuildReport(regressedCorpus);
        var transcriptFailures = report.Queries
            .Where(query => query.ExpectedClusterId == "cluster-transcript-notes" && !query.PassedTop3Recall)
            .ToList();

        Assert.NotEmpty(transcriptFailures);
    }

    [Fact]
    public void Recall_harness_json_report_matches_the_committed_verification_snapshot()
    {
        var report = RecallHarnessSupport.BuildPassingReport();
        var expectedPath = RecallHarnessSupport.ResolveRepoFile("docs/verification/12.7-search-recall-harness.json");

        RecallHarnessSupport.AssertJsonMatches(expectedPath, RecallHarnessSupport.ToJson(report));
    }
}
