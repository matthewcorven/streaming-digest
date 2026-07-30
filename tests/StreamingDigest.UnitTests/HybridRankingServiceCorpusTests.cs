using Bogus;
using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class HybridRankingServiceCorpusTests
{
    [Fact]
    public void Rank_handles_balanced_bogus_corpus()
    {
        var service = new HybridRankingService();
        Randomizer.Seed = new Random(12345);
        var faker = new Faker("en");
        var clusters = Enumerable.Range(1, 24)
            .Select(index => CreateCluster(faker, index, documentCount: 4, recentOpenCount: faker.Random.Int(0, 3)))
            .ToArray();

        var highSignalCluster = CreateCluster(faker, 999, documentCount: 4, recentOpenCount: 2, forcedTextScore: 0.95, forcedVectorScore: 0.95);
        highSignalCluster = highSignalCluster with { Id = "cluster-high-signal" };
        var ranked = service.Rank(clusters.Concat(new[] { highSignalCluster }).ToArray());

        Assert.Equal(clusters.Length + 1, ranked.Count);
        Assert.True(ranked.Zip(ranked.Skip(1), (current, next) => current.Score >= next.Score).All(match => match));
        Assert.All(ranked, result => Assert.InRange(result.Score, 0.0, 1.0));
        Assert.All(ranked, result => Assert.InRange(result.RelativeSimilarityPercent, 0.0, 100.0));
        Assert.Equal("cluster-high-signal", ranked[0].ClusterId);
    }

    [Fact]
    public void Rank_handles_adversarial_bogus_corpus_with_ties_and_clipping()
    {
        var service = new HybridRankingService();
        Randomizer.Seed = new Random(54321);
        var faker = new Faker("en");
        var clusters = Enumerable.Range(1, 30)
            .Select(index => CreateCluster(faker, index, documentCount: 3, recentOpenCount: faker.Random.Int(0, 2), forcedTextScore: 0.55, forcedVectorScore: 0.55))
            .ToArray();

        var boostedCluster = CreateCluster(faker, 500, documentCount: 3, recentOpenCount: 10, hasMatchingNote: true, forcedTextScore: 0.95, forcedVectorScore: 0.95);
        boostedCluster = boostedCluster with { Id = "cluster-boosted" };
        var ranked = service.Rank(clusters.Concat(new[] { boostedCluster }).ToArray());

        Assert.Equal(clusters.Length + 1, ranked.Count);
        Assert.Equal(1.0, ranked[0].Score);
        Assert.Equal("cluster-boosted", ranked[0].ClusterId);
        Assert.Equal(0.08, ranked[0].ScoreComponents.NoteBoost);
        Assert.Equal(0.05, ranked[0].ScoreComponents.InteractionBoost);
        Assert.All(ranked, result => Assert.InRange(result.Score, 0.0, 1.0));
    }

    private static HybridClusterCandidate CreateCluster(
        Faker faker,
        int index,
        int documentCount,
        int recentOpenCount,
        bool hasMatchingNote = false,
        double? forcedTextScore = null,
        double? forcedVectorScore = null)
    {
        var documents = Enumerable.Range(0, documentCount)
            .Select(documentIndex =>
            {
                var textScore = forcedTextScore ?? faker.Random.Double(0.0, 0.9);
                var vectorScore = forcedVectorScore ?? faker.Random.Double(0.0, 0.9);
                var documentType = documentIndex % 2 == 0 ? "title" : "transcript_chunk";

                return new HybridDocumentCandidate(
                    Id: $"doc-{index}-{documentIndex}",
                    DocumentType: documentType,
                    TextScore: textScore,
                    VectorScore: vectorScore,
                    MatchedFields: new[] { documentType, "body" },
                    Snippet: faker.Lorem.Sentence());
            })
            .ToArray();

        return new HybridClusterCandidate(
            Id: $"cluster-{index}",
            Title: faker.Company.CompanyName(),
            Documents: documents,
            RecentOpenCount: recentOpenCount,
            HasMatchingNote: hasMatchingNote);
    }
}
