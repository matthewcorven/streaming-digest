namespace StreamingDigest.Application;

public static class SearchUiCorpusCatalog
{
    public static IReadOnlyList<SearchCorpusClusterSeed> CreateDefaultFixtureCorpus()
    {
        return
        [
            new(
                Id: "cluster-search-ui",
                Title: "Designing a search-first video knowledge base",
                Channel: "Tonbis AI Garage",
                PublishDate: new DateTimeOffset(2024, 1, 12, 10, 30, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: true,
                HasNotes: true,
                IngestionStatus: "processed",
                ProcessingStatus: "processed",
                CanRetry: false,
                MatchesInsideCount: 12,
                PrimaryMatch: "Search, repo, website, and note signals all surfaced the same cluster.",
                PrimaryMatchTimestamp: "06:34",
                ScreenshotUrl: "/images/placeholder-thumbnail.png",
                RepositoryLinks: ["https://github.com/matthewcorven/streaming-digest"],
                WebsiteLinks: ["https://docs.streaming-digest.dev/search"],
                ProcessingWarnings: ["Transcript snippet is partial while the model is still indexing the full transcript."],
                Submatches:
                [
                    new SearchCorpusSubmatchSeed("Search experience walkthrough", "segment", "The transcript mentions a project idea search flow and a repo link to the matching cluster.", "06:34", "/watch/cluster-search-ui#t=404"),
                    new SearchCorpusSubmatchSeed("Repository note boost", "note", "A note attached to the video adds an extra boost for search and curation workflows.", "10:06", "/notes/cluster-search-ui"),
                    new SearchCorpusSubmatchSeed("Website summary match", "website", "The website summary includes ranking weights and search result clustering guidance.", "12:12", "https://docs.streaming-digest.dev/search")
                ],
                RelatedItems:
                [
                    new SearchCorpusRelatedItemSeed("Repository README: search indexing", "repository", 91.4, "https://github.com/matthewcorven/streaming-digest", "Repository metadata contains the most overlap for search and vector ranking."),
                    new SearchCorpusRelatedItemSeed("Note: curation workflow", "note", 84.3, "/notes/cluster-search-ui", "The note explains why search quality depends on ranking weights and related items."),
                    new SearchCorpusRelatedItemSeed("Website page: ranking guidance", "website", 79.5, "https://docs.streaming-digest.dev/search", "The page is similar because it explains how text and vector ranking should be blended.")
                ],
                RecentOpenCount: 8,
                HasMatchingNote: true,
                Documents:
                [
                    new SearchCorpusDocumentSeed("segment-1", "segment", "A practical walkthrough of a search-first workflow for project ideas and repository links.", "Project idea search surfaced a cluster with transcript, repo, and note evidence.", null, ["title", "transcript"], 0.76, 0.78),
                    new SearchCorpusDocumentSeed("segment-2", "transcript", "A video transcript section explaining how search results cluster by video and include timestamp links.", "The transcript highlights how notes and repository links reinforce the best match.", null, ["transcript"], 0.71, 0.7),
                    new SearchCorpusDocumentSeed("repo-1", "repository", "The repository README explains search indexing, ranking, and vector retrieval for the product.", "Repository search terms align with the query and the note content.", null, ["repository"], 0.68, 0.74),
                    new SearchCorpusDocumentSeed("website-1", "website", "The website page explains why text and vector weighting should be tuned for vague queries.", "Website content overlaps on search, ranking, and recent-search behavior.", null, ["website"], 0.67, 0.73),
                    new SearchCorpusDocumentSeed("note-1", "note", "A note that captures the search curation experience and why user signals boost the right cluster.", "The note contains the user-facing language from the query.", null, ["note"], 0.72, 0.76)
                ]),
            new(
                Id: "cluster-ranking-weights",
                Title: "Balancing text and vector ranking weights",
                Channel: "Microsoft Build",
                PublishDate: new DateTimeOffset(2024, 2, 22, 14, 20, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: false,
                HasNotes: false,
                IngestionStatus: "stale",
                ProcessingStatus: "stale",
                CanRetry: true,
                MatchesInsideCount: 7,
                PrimaryMatch: "The video explains how ranking weights shift from lexical to semantic matching.",
                PrimaryMatchTimestamp: "12:04",
                ScreenshotUrl: string.Empty,
                RepositoryLinks: [],
                WebsiteLinks: ["https://learn.microsoft.com/azure/search"],
                ProcessingWarnings: ["The cluster is stale because the transcript was reprocessed after the last ranking pass."],
                Submatches:
                [
                    new SearchCorpusSubmatchSeed("Weight tuning walkthrough", "segment", "The segment compares lexical and vector ranking for undefined or vague prompts.", "12:04", "/watch/cluster-ranking-weights#t=724"),
                    new SearchCorpusSubmatchSeed("Similarity explanation", "segment", "The transcript describes why relative similarity is useful for cluster trust.", "19:11", "/watch/cluster-ranking-weights#t=1151")
                ],
                RelatedItems:
                [
                    new SearchCorpusRelatedItemSeed("Learn: hybrid retrieval", "website", 76.8, "https://learn.microsoft.com/azure/search", "The page is similar because it describes hybrid retrieval and weighted scoring."),
                    new SearchCorpusRelatedItemSeed("Transcript segment: tuning weights", "segment", 72.1, "/watch/cluster-ranking-weights#t=724", "The transcript segment is very close to the ranking-weight language in the query.")
                ],
                RecentOpenCount: 4,
                HasMatchingNote: false,
                Documents:
                [
                    new SearchCorpusDocumentSeed("rank-1", "segment", "A deep dive into text and vector weighting for vague prompts.", "The segment compares lexical and semantic matching strength.", null, ["transcript"], 0.74, 0.79),
                    new SearchCorpusDocumentSeed("rank-2", "transcript", "The transcript mentions relative similarity percentages and why they matter for trust.", "It is aligned with the ranking explanation in the query.", null, ["transcript"], 0.69, 0.74),
                    new SearchCorpusDocumentSeed("rank-3", "website", "A website article that covers hybrid search and scoring trade-offs.", "The article includes language about applying ranking weights to search results.", null, ["website"], 0.66, 0.76)
                ]),
            new(
                Id: "cluster-transcript-notes",
                Title: "Using notes and transcripts to recover hidden context",
                Channel: "The Practical AI Channel",
                PublishDate: new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: false,
                HasNotes: true,
                IngestionStatus: "failed",
                ProcessingStatus: "failed",
                CanRetry: true,
                MatchesInsideCount: 5,
                PrimaryMatch: "The video combines transcript snippets and note attachments to recover weak keyword matches.",
                PrimaryMatchTimestamp: "03:22",
                ScreenshotUrl: string.Empty,
                RepositoryLinks: [],
                WebsiteLinks: ["https://example.com/notes-and-search"],
                ProcessingWarnings: ["The embedding pass failed for the linked note, so the score is provisional."],
                Submatches:
                [
                    new SearchCorpusSubmatchSeed("Note-assisted recall", "note", "The note improves recall when the transcript is sparse.", "03:22", "/notes/cluster-transcript-notes"),
                    new SearchCorpusSubmatchSeed("Transcript recovery example", "transcript", "The transcript shows how weak terms still recover the right cluster when notes are involved.", "08:18", "/watch/cluster-transcript-notes#t=498")
                ],
                RelatedItems:
                [
                    new SearchCorpusRelatedItemSeed("Note memory reference", "note", 68.9, "/notes/cluster-transcript-notes", "The note is a close match because it mentions search recall and hidden context."),
                    new SearchCorpusRelatedItemSeed("Website: notes and search", "website", 66.1, "https://example.com/notes-and-search", "The site focuses on note-assisted recall and the same vocabulary as the query.")
                ],
                RecentOpenCount: 2,
                HasMatchingNote: true,
                Documents:
                [
                    new SearchCorpusDocumentSeed("transcript-1", "transcript", "The video explains how notes and transcript context can recover latent ideas.", "The query over-indexes on note and transcript vocabulary.", null, ["transcript"], 0.72, 0.71),
                    new SearchCorpusDocumentSeed("note-2", "note", "A short note highlighting recall for vague project ideas and hidden context.", "The note overlaps with the search language used in the query.", null, ["note"], 0.73, 0.75),
                    new SearchCorpusDocumentSeed("website-2", "website", "The website page covers note-assisted search for long-form video and transcripts.", "The page contains similar language about hidden context and recall.", null, ["website"], 0.67, 0.68)
                ])
        ];
    }
}

public sealed record SearchCorpusClusterSeed(
    string Id,
    string Title,
    string Channel,
    DateTimeOffset PublishDate,
    string ResultType,
    bool HasTranscript,
    bool HasRepo,
    bool HasNotes,
    string IngestionStatus,
    string ProcessingStatus,
    bool CanRetry,
    int MatchesInsideCount,
    string? PrimaryMatch,
    string? PrimaryMatchTimestamp,
    string? ScreenshotUrl,
    IReadOnlyList<string> RepositoryLinks,
    IReadOnlyList<string> WebsiteLinks,
    IReadOnlyList<string> ProcessingWarnings,
    IReadOnlyList<SearchCorpusSubmatchSeed> Submatches,
    IReadOnlyList<SearchCorpusRelatedItemSeed> RelatedItems,
    int RecentOpenCount,
    bool HasMatchingNote,
    IReadOnlyList<SearchCorpusDocumentSeed> Documents);

public sealed record SearchCorpusDocumentSeed(
    string Id,
    string DocumentType,
    string Text,
    string? Snippet,
    string? Field,
    IReadOnlyList<string>? MatchedFields,
    double TextScore,
    double VectorScore);

public sealed record SearchCorpusSubmatchSeed(string Title, string Type, string Detail, string? TimestampLabel, string? Url);

public sealed record SearchCorpusRelatedItemSeed(string Title, string Type, double RelativeSimilarityPercent, string? Url, string? Detail);
