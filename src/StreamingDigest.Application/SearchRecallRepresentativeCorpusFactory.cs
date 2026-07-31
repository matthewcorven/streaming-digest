namespace StreamingDigest.Application;

public static class SearchRecallRepresentativeCorpusFactory
{
    private static readonly string[] SearchUiSharedTerms =
    [
        "search", "cluster", "repository", "website", "timestamp", "curation", "result card", "project idea", "knowledge base", "recall"
    ];

    private static readonly string[] SearchUiVariants =
    [
        "cataloging engineering videos for later recall",
        "organizing watch history into searchable project memory",
        "jumping from vague ideas into the right repo and website links",
        "turning transcripts into quick-launch search cards"
    ];

    private static readonly string[] RankingSharedTerms =
    [
        "ranking", "vector", "text", "weight", "hybrid", "similarity", "trust", "score", "blend", "semantic"
    ];

    private static readonly string[] RankingVariants =
    [
        "explaining why lexical and semantic signals disagree",
        "tuning relevance when vague prompts return the wrong clip",
        "reading score breakdowns instead of guessing at search quality",
        "choosing a better balance between keyword and embedding matches"
    ];

    private static readonly string[] TranscriptSharedTerms =
    [
        "note", "transcript", "hidden context", "sparse", "recall", "memory", "recovery", "vague", "idea", "context"
    ];

    private static readonly string[] TranscriptVariants =
    [
        "recovering the clip when the query barely matches the transcript",
        "leaning on notes when the spoken words are thin",
        "saving the missing context that never made it into the title",
        "finding an old video from a half-remembered thought"
    ];

    public static IReadOnlyList<SearchCorpusClusterSeed> CreateRepresentativeCorpus(int totalVideoCount = 500, int seed = 31)
    {
        if (totalVideoCount < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(totalVideoCount), "Representative recall corpora must include enough distractors to challenge top-3 recall.");
        }

        var golden = SearchUiCorpusCatalog.CreateDefaultFixtureCorpus().ToList();
        if (golden.Count >= totalVideoCount)
        {
            return golden;
        }

        var random = new Random(seed);
        var corpus = new List<SearchCorpusClusterSeed>(golden);
        var distractorCount = totalVideoCount - golden.Count;

        for (var index = 0; index < distractorCount; index++)
        {
            var anchor = golden[index % golden.Count];
            corpus.Add(CreateDistractor(anchor, index + 1, random));
        }

        return corpus;
    }

    private static SearchCorpusClusterSeed CreateDistractor(SearchCorpusClusterSeed anchor, int index, Random random)
    {
        return anchor.Id switch
        {
            "cluster-search-ui" => CreateDistractor(anchor, index, random, SearchUiSharedTerms, SearchUiVariants, hasRepoBias: true, hasNotesBias: true),
            "cluster-ranking-weights" => CreateDistractor(anchor, index, random, RankingSharedTerms, RankingVariants, hasRepoBias: false, hasNotesBias: false),
            "cluster-transcript-notes" => CreateDistractor(anchor, index, random, TranscriptSharedTerms, TranscriptVariants, hasRepoBias: false, hasNotesBias: true),
            _ => throw new InvalidOperationException($"Unexpected golden cluster id '{anchor.Id}'.")
        };
    }

    private static SearchCorpusClusterSeed CreateDistractor(
        SearchCorpusClusterSeed anchor,
        int index,
        Random random,
        IReadOnlyList<string> sharedTerms,
        IReadOnlyList<string> variants,
        bool hasRepoBias,
        bool hasNotesBias)
    {
        var variant = variants[index % variants.Count];
        var mixedTerms = PickTerms(sharedTerms, random, 4);
        var title = $"{Capitalize(mixedTerms[0])} workshop #{index:000}: {variant}";
        var details = $"{string.Join(", ", mixedTerms)} and adjacent workflow trade-offs.";
        var publishDate = anchor.PublishDate.AddDays(index + 3);
        var hasRepo = hasRepoBias && index % 2 == 0;
        var hasNotes = hasNotesBias && index % 3 == 0;
        var hasMatchingNote = hasNotes && index % 7 == 0;

        var repositoryLinks = hasRepo
            ? new[] { $"https://example.com/repos/{anchor.Id}/{index:000}" }
            : Array.Empty<string>();
        var websiteLinks = new[] { $"https://example.com/related/{anchor.Id}/{index:000}" };
        var warnings = index % 5 == 0
            ? new[] { "This distractor cluster keeps neighboring vocabulary but lacks the exact recall signals from the golden fixtures." }
            : Array.Empty<string>();

        var documents = BuildDocuments(anchor, index, random, mixedTerms, variant, hasRepo, hasNotes);
        var submatches = BuildSubmatches(anchor, index, mixedTerms, variant, hasNotes);
        var relatedItems = BuildRelatedItems(anchor, index, mixedTerms, websiteLinks[0], hasRepo, hasNotes);

        return new SearchCorpusClusterSeed(
            Id: $"{anchor.Id}-distractor-{index:000}",
            Title: title,
            Channel: $"{anchor.Channel} Adjacent Topics",
            PublishDate: publishDate,
            ResultType: anchor.ResultType,
            HasTranscript: true,
            HasRepo: hasRepo,
            HasNotes: hasNotes,
            IngestionStatus: "processed",
            ProcessingStatus: "processed",
            CanRetry: false,
            MatchesInsideCount: 2 + (index % 5),
            PrimaryMatch: $"A nearby result covering {details}",
            PrimaryMatchTimestamp: $"{2 + (index % 10):00}:{10 + (index % 50):00}",
            ScreenshotUrl: string.Empty,
            RepositoryLinks: repositoryLinks,
            WebsiteLinks: websiteLinks,
            ProcessingWarnings: warnings,
            Submatches: submatches,
            RelatedItems: relatedItems,
            RecentOpenCount: index % 3,
            HasMatchingNote: hasMatchingNote,
            Documents: documents);
    }

    private static IReadOnlyList<SearchCorpusDocumentSeed> BuildDocuments(
        SearchCorpusClusterSeed anchor,
        int index,
        Random random,
        IReadOnlyList<string> mixedTerms,
        string variant,
        bool hasRepo,
        bool hasNotes)
    {
        var transcriptBase = 0.48 + (0.04 * (index % 4));
        var vectorBase = 0.5 + (0.04 * ((index + 1) % 4));
        var documents = new List<SearchCorpusDocumentSeed>
        {
            new(
                Id: $"{anchor.Id}-d-tx-{index:000}",
                DocumentType: "transcript",
                Text: $"A transcript about {variant}. It circles around {string.Join(", ", mixedTerms)} without reaching the exact fixture workflow.",
                Snippet: $"Nearby transcript signal: {mixedTerms[0]}, {mixedTerms[1]}, and adjacent trade-offs.",
                Field: null,
                MatchedFields: ["transcript"],
                TextScore: transcriptBase,
                VectorScore: vectorBase),
            new(
                Id: $"{anchor.Id}-d-web-{index:000}",
                DocumentType: "website",
                Text: $"A linked page explores {mixedTerms[2]} and {mixedTerms[3]} for teams working on {anchor.Channel}.",
                Snippet: "The page shares vocabulary with the fixture query but points at a different adjacent discussion.",
                Field: null,
                MatchedFields: ["website"],
                TextScore: 0.46 + (0.03 * ((index + 2) % 4)),
                VectorScore: 0.49 + (0.03 * ((index + 3) % 4)))
        };

        if (hasRepo)
        {
            documents.Add(new SearchCorpusDocumentSeed(
                Id: $"{anchor.Id}-d-repo-{index:000}",
                DocumentType: "repository",
                Text: $"Repository notes about {mixedTerms[0]} and {mixedTerms[2]} with only partial overlap to the intended recall path.",
                Snippet: "Repository evidence is present, but the core fixture-specific wording is missing.",
                Field: null,
                MatchedFields: ["repository"],
                TextScore: 0.44 + (0.02 * random.Next(0, 5)),
                VectorScore: 0.47 + (0.03 * random.Next(0, 4))));
        }

        if (hasNotes)
        {
            documents.Add(new SearchCorpusDocumentSeed(
                Id: $"{anchor.Id}-d-note-{index:000}",
                DocumentType: "note",
                Text: $"A short note about {mixedTerms[1]} and {variant}. It hints at the same theme but not the exact solved problem.",
                Snippet: "The note keeps the topic close enough to compete while staying a distractor.",
                Field: null,
                MatchedFields: ["note"],
                TextScore: 0.47 + (0.02 * random.Next(0, 5)),
                VectorScore: 0.5 + (0.02 * random.Next(0, 5))));
        }

        return documents;
    }

    private static IReadOnlyList<SearchCorpusSubmatchSeed> BuildSubmatches(
        SearchCorpusClusterSeed anchor,
        int index,
        IReadOnlyList<string> mixedTerms,
        string variant,
        bool hasNotes)
    {
        var matches = new List<SearchCorpusSubmatchSeed>
        {
            new($"Adjacent segment {index:000}", "segment", $"The discussion covers {variant} with neighboring emphasis on {mixedTerms[0]} and {mixedTerms[1]}.", $"{3 + (index % 9):00}:{15 + (index % 40):00}", $"/watch/{anchor.Id}-distractor-{index:000}")
        };

        if (hasNotes)
        {
            matches.Add(new SearchCorpusSubmatchSeed($"Adjacent note {index:000}", "note", $"A note preserves side observations about {mixedTerms[2]} without the exact recall target.", null, $"/notes/{anchor.Id}-distractor-{index:000}"));
        }

        return matches;
    }

    private static IReadOnlyList<SearchCorpusRelatedItemSeed> BuildRelatedItems(
        SearchCorpusClusterSeed anchor,
        int index,
        IReadOnlyList<string> mixedTerms,
        string websiteUrl,
        bool hasRepo,
        bool hasNotes)
    {
        var items = new List<SearchCorpusRelatedItemSeed>
        {
            new($"Website background {index:000}", "website", 62.0 - (index % 7), websiteUrl, $"Adjacent material about {mixedTerms[0]} and {mixedTerms[1]}.")
        };

        if (hasRepo)
        {
            items.Add(new SearchCorpusRelatedItemSeed($"Repository background {index:000}", "repository", 58.0 - (index % 6), $"https://example.com/repos/{anchor.Id}/{index:000}", "Repository overlap exists, but only part of the intended workflow is present."));
        }

        if (hasNotes)
        {
            items.Add(new SearchCorpusRelatedItemSeed($"Note background {index:000}", "note", 55.0 - (index % 5), $"/notes/{anchor.Id}-distractor-{index:000}", "The note stays topically adjacent rather than matching the intended cluster."));
        }

        return items;
    }

    private static string[] PickTerms(IReadOnlyList<string> source, Random random, int count)
    {
        return source
            .OrderBy(_ => random.Next())
            .Take(count)
            .ToArray();
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
