using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchLatencyBenchmarkTests
{
    [Fact]
    public void Representative_benchmark_meets_the_task_12_8_latency_targets()
    {
        var report = SearchLatencyBenchmark.RunRepresentativeBenchmark();

        Assert.Equal([500, 2000], report.Corpora.Select(corpus => corpus.VideoCount).ToArray());
        Assert.All(report.Corpora, corpus => Assert.True(corpus.MeetsLatencyTarget, $"Expected corpus {corpus.VideoCount} to meet latency targets, but measured p50={corpus.P50Ms:0.000}ms and p95={corpus.P95Ms:0.000}ms."));
    }

    [Fact]
    public void Representative_benchmark_preserves_search_document_and_embedding_scale()
    {
        var report = SearchLatencyBenchmark.RunRepresentativeBenchmark(new SearchLatencyBenchmarkOptions
        {
            QueryCount = 24,
            MeasurementPasses = 1,
            WarmupQueryCount = 3
        });

        var smallCorpus = report.Corpora.Single(corpus => corpus.VideoCount == 500);
        var largeCorpus = report.Corpora.Single(corpus => corpus.VideoCount == 2000);

        Assert.True(smallCorpus.EntityCounts.SearchDocuments >= 1200);
        Assert.True(largeCorpus.EntityCounts.SearchDocuments > smallCorpus.EntityCounts.SearchDocuments);
        Assert.Equal(smallCorpus.EntityCounts.SearchDocuments, smallCorpus.EntityCounts.Embeddings);
        Assert.Equal(largeCorpus.EntityCounts.SearchDocuments, largeCorpus.EntityCounts.Embeddings);
    }
}
