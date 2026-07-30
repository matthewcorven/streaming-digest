namespace StreamingDigest.Application;

public sealed class SearchUiService
{
    private static readonly TimeSpan RecentSearchStateLifetime = TimeSpan.FromHours(2);
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RecentSearchState> _recentSearchStates = new(StringComparer.OrdinalIgnoreCase);
    private SearchUiSettings _settings = SearchUiSettings.Default;

    public SearchUiService(HybridRankingService? rankingService = null)
    {
    }

    public SearchUiSettings GetSettings()
    {
        lock (_syncRoot)
        {
            return CloneSettings(_settings);
        }
    }

    public void UpdateSettings(SearchUiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(settings.TextWeight) || !double.IsFinite(settings.VectorWeight))
        {
            throw new ArgumentException("Ranking weights must be finite values.", nameof(settings));
        }

        if (settings.TextWeight < 0 || settings.VectorWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Ranking weights must be non-negative.");
        }

        if (settings.TextWeight + settings.VectorWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Text and vector weights must sum to a positive value.");
        }

        lock (_syncRoot)
        {
            _settings = new SearchUiSettings
            {
                TextWeight = settings.TextWeight,
                VectorWeight = settings.VectorWeight
            };
        }
    }

    public SearchResponse Search(SearchRequest request) => Search(request, stateKey: null);

    public SearchResponse Search(SearchRequest request, string? stateKey)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_syncRoot)
        {
            var normalizedQuery = string.IsNullOrWhiteSpace(request.Query) ? "project idea search" : request.Query.Trim();
            var terms = ExtractTerms(normalizedQuery);
            var activeFilters = request.Filters ?? SearchFilters.Empty;
            var state = GetOrCreateRecentSearchState(stateKey);
            var effectiveSettings = CloneSettings(_settings);

            var candidateSeeds = CreateCandidateSeeds()
                .Where(seed => MatchesFilters(seed, activeFilters))
                .ToList();

            if (candidateSeeds.Count == 0)
            {
                return CreateEmptyResponse(normalizedQuery, state);
            }

            var rankedClusters = candidateSeeds
                .Select(seed => CreateClusterCandidate(seed, normalizedQuery, terms))
                .ToList();

            var rankingService = CreateRankingService(effectiveSettings);
            var rankingResults = rankingService.Rank(
                rankedClusters,
                relativeSimilarityPoolScores: rankedClusters.Select(cluster => cluster.Documents.Max(document => document.VectorScore)).ToList());

            var results = rankingResults
                .Select(result => MapResult(result, candidateSeeds.First(seed => string.Equals(seed.Id, result.ClusterId, StringComparison.OrdinalIgnoreCase))))
                .ToList();

            RegisterRecentSearch(state, normalizedQuery);

            return new SearchResponse
            {
                Query = normalizedQuery,
                Results = results,
                RecentSearches = GetRecentSearches(stateKey).ToList(),
                Settings = CloneSettings(_settings),
                Summary = results.Count == 0
                    ? "No clusters matched the current filters."
                    : $"Showing {results.Count} clustered video result{(results.Count == 1 ? string.Empty : "s")} for '{normalizedQuery}'."
            };
        }
    }

    public IReadOnlyList<string> GetRecentSearches() => GetRecentSearches(stateKey: null);

    public IReadOnlyList<string> GetRecentSearches(string? stateKey)
    {
        lock (_syncRoot)
        {
            PruneExpiredRecentSearchStates();
            return GetOrCreateRecentSearchState(stateKey).RecentSearches
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }
    }

    public void ClearRecentSearches() => ClearRecentSearches(stateKey: null);

    public void ClearRecentSearches(string? stateKey)
    {
        lock (_syncRoot)
        {
            RemoveRecentSearchState(stateKey);
        }
    }

    public void RemoveState(string? stateKey)
    {
        lock (_syncRoot)
        {
            RemoveRecentSearchState(stateKey);
        }
    }

    private void RegisterRecentSearch(RecentSearchState state, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var normalizedQuery = query.Trim();
        lock (_syncRoot)
        {
            var retainedValues = new List<string>();
            while (state.RecentSearches.Count > 0)
            {
                var current = state.RecentSearches.Dequeue();
                if (string.IsNullOrWhiteSpace(current))
                {
                    continue;
                }

                var trimmedCurrent = current.Trim();
                if (string.Equals(trimmedCurrent, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                retainedValues.Add(trimmedCurrent);
            }

            foreach (var retainedValue in retainedValues)
            {
                state.RecentSearches.Enqueue(retainedValue);
            }

            state.RecentSearches.Enqueue(normalizedQuery);

            while (state.RecentSearches.Count > 8)
            {
                state.RecentSearches.Dequeue();
            }
        }
    }

    private RecentSearchState GetOrCreateRecentSearchState(string? stateKey)
    {
        PruneExpiredRecentSearchStates();
        var effectiveStateKey = NormalizeStateKey(stateKey);
        if (_recentSearchStates.TryGetValue(effectiveStateKey, out var state))
        {
            state.LastAccessedAt = DateTimeOffset.UtcNow;
            return state;
        }

        var createdState = new RecentSearchState();
        _recentSearchStates[effectiveStateKey] = createdState;
        return createdState;
    }

    private void RemoveRecentSearchState(string? stateKey)
    {
        _recentSearchStates.Remove(NormalizeStateKey(stateKey));
    }

    private void PruneExpiredRecentSearchStates()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(RecentSearchStateLifetime);
        foreach (var expiredState in _recentSearchStates.Where(state => state.Value.LastAccessedAt < cutoff).Select(state => state.Key).ToList())
        {
            _recentSearchStates.Remove(expiredState);
        }
    }

    private SearchResponse CreateEmptyResponse(string query, RecentSearchState state)
    {
        return new SearchResponse
        {
            Query = query,
            Results = Array.Empty<SearchResultClusterResponse>(),
            RecentSearches = state.RecentSearches
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList(),
            Settings = CloneSettings(_settings),
            Summary = "No clusters matched the current filters."
        };
    }

    private HybridRankingService CreateRankingService(SearchUiSettings settings)
    {
        return new HybridRankingService(new HybridRankingOptions(
            TextWeight: settings.TextWeight,
            VectorWeight: settings.VectorWeight,
            AggregateMaxWeight: HybridRankingOptions.Default.AggregateMaxWeight,
            AggregateTopThreeWeight: HybridRankingOptions.Default.AggregateTopThreeWeight,
            AggregateCoverageWeight: HybridRankingOptions.Default.AggregateCoverageWeight,
            NoteBoost: HybridRankingOptions.Default.NoteBoost,
            InteractionBoostCap: HybridRankingOptions.Default.InteractionBoostCap));
    }

    private static SearchUiSettings CloneSettings(SearchUiSettings? settings)
    {
        return new SearchUiSettings
        {
            TextWeight = settings?.TextWeight ?? SearchUiSettings.Default.TextWeight,
            VectorWeight = settings?.VectorWeight ?? SearchUiSettings.Default.VectorWeight
        };
    }

    private static string NormalizeStateKey(string? stateKey)
    {
        return string.IsNullOrWhiteSpace(stateKey) ? "__default__" : stateKey;
    }

    private sealed class RecentSearchState
    {
        public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;
        public Queue<string> RecentSearches { get; } = new();
    }

    private static bool MatchesFilters(SearchClusterSeed seed, SearchFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Channel) && !string.Equals(seed.Channel, filters.Channel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.ResultType) && !string.Equals(seed.ResultType, filters.ResultType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filters.HasTranscript is true && !seed.HasTranscript)
        {
            return false;
        }

        if (filters.HasRepo is true && !seed.HasRepo)
        {
            return false;
        }

        if (filters.HasNotes is true && !seed.HasNotes)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.IngestionStatus) && !string.Equals(seed.IngestionStatus, filters.IngestionStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filters.DateRange))
        {
            var now = DateTimeOffset.UtcNow;
            if (filters.DateRange.Equals("last-week", StringComparison.OrdinalIgnoreCase))
            {
                if (seed.PublishDate < now.AddDays(-7))
                {
                    return false;
                }
            }
            else if (filters.DateRange.Equals("last-month", StringComparison.OrdinalIgnoreCase))
            {
                if (seed.PublishDate < now.AddDays(-30))
                {
                    return false;
                }
            }
            else if (filters.DateRange.Equals("older", StringComparison.OrdinalIgnoreCase))
            {
                if (seed.PublishDate >= now.AddDays(-30))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static HybridClusterCandidate CreateClusterCandidate(SearchClusterSeed seed, string query, IReadOnlyList<string> terms)
    {
        var documents = seed.Documents.Select(document =>
        {
            var textScore = ScoreTextMatch(query, terms, document.Text, document.Snippet);
            var vectorScore = ScoreVectorMatch(query, terms, document.Text, document.Snippet);
            return new HybridDocumentCandidate(
                Id: document.Id,
                DocumentType: document.DocumentType,
                TextScore: textScore,
                VectorScore: vectorScore,
                MatchedFields: document.MatchedFields,
                Snippet: document.Snippet);
        }).ToList();

        return new HybridClusterCandidate(
            Id: seed.Id,
            Title: seed.Title,
            Documents: documents,
            RecentOpenCount: seed.RecentOpenCount,
            HasMatchingNote: seed.HasMatchingNote);
    }

    private static SearchResultClusterResponse MapResult(HybridClusterRankingResult ranking, SearchClusterSeed seed)
    {
        var primarySnippet = seed.Submatches.FirstOrDefault();
        return new SearchResultClusterResponse
        {
            ClusterId = ranking.ClusterId,
            Title = seed.Title,
            Channel = seed.Channel,
            PublishDate = seed.PublishDate,
            ResultType = seed.ResultType,
            HasTranscript = seed.HasTranscript,
            HasRepo = seed.HasRepo,
            HasNotes = seed.HasNotes,
            HasScreenshot = !string.IsNullOrWhiteSpace(seed.ScreenshotUrl),
            ProcessingStatus = seed.ProcessingStatus,
            CanRetry = seed.CanRetry,
            MatchesInsideCount = seed.MatchesInsideCount,
            PrimaryMatch = primarySnippet?.Detail ?? seed.PrimaryMatch,
            PrimaryMatchTimestamp = primarySnippet?.TimestampLabel ?? seed.PrimaryMatchTimestamp,
            Score = ranking.Score,
            ScoreExplanation = ranking.Explanation,
            RelativeSimilarityPercent = ranking.RelativeSimilarityPercent,
            MatchedFields = ranking.MatchedFields,
            ProcessingWarnings = seed.ProcessingWarnings,
            RepositoryLinks = seed.RepositoryLinks,
            WebsiteLinks = seed.WebsiteLinks,
            ScreenshotUrl = seed.ScreenshotUrl,
            Submatches = seed.Submatches.Select(item => new SearchSubmatchResponse
            {
                Title = item.Title,
                Type = item.Type,
                Detail = item.Detail,
                TimestampLabel = item.TimestampLabel,
                Url = item.Url
            }).ToList(),
            RelatedItems = seed.RelatedItems.Select(item => new SearchRelatedItemResponse
            {
                Title = item.Title,
                Type = item.Type,
                Url = item.Url,
                RelativeSimilarityPercent = item.RelativeSimilarityPercent,
                Detail = item.Detail
            }).ToList(),
            ScoreComponents = new SearchScoreComponentsResponse
            {
                BaseScore = ranking.ScoreComponents.BaseScore,
                MaxDocumentScore = ranking.ScoreComponents.MaxDocumentScore,
                AverageTopThreeDocumentScore = ranking.ScoreComponents.AverageTopThreeDocumentScore,
                CoverageScore = ranking.ScoreComponents.CoverageScore,
                NoteBoost = ranking.ScoreComponents.NoteBoost,
                InteractionBoost = ranking.ScoreComponents.InteractionBoost
            }
        };
    }

    private static double ScoreTextMatch(string query, IReadOnlyList<string> terms, string text, string? snippet)
    {
        var haystack = string.Join(" ", query, text, snippet ?? string.Empty).ToLowerInvariant();
        var score = 0.0;

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.3;
            }
        }

        if (haystack.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15;
        }

        if (haystack.Contains("repo", StringComparison.OrdinalIgnoreCase) || haystack.Contains("repository", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        if (haystack.Contains("note", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        return Math.Min(1.0, score);
    }

    private static double ScoreVectorMatch(string query, IReadOnlyList<string> terms, string text, string? snippet)
    {
        var haystack = string.Join(" ", query, text, snippet ?? string.Empty).ToLowerInvariant();
        var score = 0.0;

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
            }
        }

        if (haystack.Contains("hybrid", StringComparison.OrdinalIgnoreCase) || haystack.Contains("vector", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15;
        }

        if (haystack.Contains("transcript", StringComparison.OrdinalIgnoreCase) || haystack.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        if (haystack.Contains("website", StringComparison.OrdinalIgnoreCase) || haystack.Contains("repository", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15;
        }

        return Math.Min(1.0, score);
    }

    private static IReadOnlyList<string> ExtractTerms(string query)
    {
        return query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 2)
            .Select(part => part.Trim().Trim('.', ',', ';', ':'))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SearchClusterSeed> CreateCandidateSeeds()
    {
        return new List<SearchClusterSeed>
        {
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
                RepositoryLinks: new[] { "https://github.com/matthewcorven/streaming-digest" },
                WebsiteLinks: new[] { "https://docs.streaming-digest.dev/search" },
                ProcessingWarnings: new[] { "Transcript snippet is partial while the model is still indexing the full transcript." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Search experience walkthrough", "segment", "The transcript mentions a project idea search flow and a repo link to the matching cluster.", "06:34", "/watch/cluster-search-ui#t=404"),
                    new SearchSubmatchSeed("Repository note boost", "note", "A note attached to the video adds an extra boost for search and curation workflows.", "10:06", "/notes/cluster-search-ui"),
                    new SearchSubmatchSeed("Website summary match", "website", "The website summary includes ranking weights and search result clustering guidance.", "12:12", "https://docs.streaming-digest.dev/search")
                },
                RelatedItems: new[]
                {
                    new SearchRelatedItemSeed("Repository README: search indexing", "repository", 91.4, "https://github.com/matthewcorven/streaming-digest", "Repository metadata contains the most overlap for search and vector ranking."),
                    new SearchRelatedItemSeed("Note: curation workflow", "note", 84.3, "/notes/cluster-search-ui", "The note explains why search quality depends on ranking weights and related items."),
                    new SearchRelatedItemSeed("Website page: ranking guidance", "website", 79.5, "https://docs.streaming-digest.dev/search", "The page is similar because it explains how text and vector ranking should be blended.")
                },
                RecentOpenCount: 8,
                HasMatchingNote: true,
                Documents: new[]
                {
                    new SearchDocumentSeed("segment-1", "segment", "A practical walkthrough of a search-first workflow for project ideas and repository links.", "Project idea search surfaced a cluster with transcript, repo, and note evidence.", null, new[] { "title", "transcript" }, 0.95, 0.82),
                    new SearchDocumentSeed("segment-2", "transcript", "A video transcript section explaining how search results cluster by video and include timestamp links.", "The transcript highlights how notes and repository links reinforce the best match.", null, new[] { "transcript" }, 0.88, 0.76),
                    new SearchDocumentSeed("repo-1", "repository", "The repository README explains search indexing, ranking, and vector retrieval for the product.", "Repository search terms align with the query and the note content.", null, new[] { "repository" }, 0.84, 0.9),
                    new SearchDocumentSeed("website-1", "website", "The website page explains why text and vector weighting should be tuned for vague queries.", "Website content overlaps on search, ranking, and recent-search behavior.", null, new[] { "website" }, 0.81, 0.86),
                    new SearchDocumentSeed("note-1", "note", "A note that captures the search curation experience and why user signals boost the right cluster.", "The note contains the user-facing language from the query.", null, new[] { "note" }, 0.79, 0.84)
                }),
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
                RepositoryLinks: Array.Empty<string>(),
                WebsiteLinks: new[] { "https://learn.microsoft.com/azure/search" },
                ProcessingWarnings: new[] { "The cluster is stale because the transcript was reprocessed after the last ranking pass." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Weight tuning walkthrough", "segment", "The segment compares lexical and vector ranking for undefined or vague prompts.", "12:04", "/watch/cluster-ranking-weights#t=724"),
                    new SearchSubmatchSeed("Similarity explanation", "segment", "The transcript describes why relative similarity is useful for cluster trust.", "19:11", "/watch/cluster-ranking-weights#t=1151")
                },
                RelatedItems: new[]
                {
                    new SearchRelatedItemSeed("Learn: hybrid retrieval", "website", 76.8, "https://learn.microsoft.com/azure/search", "The page is similar because it describes hybrid retrieval and weighted scoring."),
                    new SearchRelatedItemSeed("Transcript segment: tuning weights", "segment", 72.1, "/watch/cluster-ranking-weights#t=724", "The transcript segment is very close to the ranking-weight language in the query.")
                },
                RecentOpenCount: 4,
                HasMatchingNote: false,
                Documents: new[]
                {
                    new SearchDocumentSeed("rank-1", "segment", "A deep dive into text and vector weighting for vague prompts.", "The segment compares lexical and semantic matching strength.", null, new[] { "transcript" }, 0.76, 0.88),
                    new SearchDocumentSeed("rank-2", "transcript", "The transcript mentions relative similarity percentages and why they matter for trust.", "It is aligned with the ranking explanation in the query.", null, new[] { "transcript" }, 0.72, 0.79),
                    new SearchDocumentSeed("rank-3", "website", "A website article that covers hybrid search and scoring trade-offs.", "The article includes language about applying ranking weights to search results.", null, new[] { "website" }, 0.68, 0.84)
                }),
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
                RepositoryLinks: Array.Empty<string>(),
                WebsiteLinks: new[] { "https://example.com/notes-and-search" },
                ProcessingWarnings: new[] { "The embedding pass failed for the linked note, so the score is provisional." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Note-assisted recall", "note", "The note improves recall when the transcript is sparse.", "03:22", "/notes/cluster-transcript-notes"),
                    new SearchSubmatchSeed("Transcript recovery example", "transcript", "The transcript shows how weak terms still recover the right cluster when notes are involved.", "08:18", "/watch/cluster-transcript-notes#t=498")
                },
                RelatedItems: new[]
                {
                    new SearchRelatedItemSeed("Note memory reference", "note", 68.9, "/notes/cluster-transcript-notes", "The note is a close match because it mentions search recall and hidden context."),
                    new SearchRelatedItemSeed("Website: notes and search", "website", 66.1, "https://example.com/notes-and-search", "The site focuses on note-assisted recall and the same vocabulary as the query.")
                },
                RecentOpenCount: 2,
                HasMatchingNote: true,
                Documents: new[]
                {
                    new SearchDocumentSeed("transcript-1", "transcript", "The video explains how notes and transcript context can recover latent ideas.", "The query over-indexes on note and transcript vocabulary.", null, new[] { "transcript" }, 0.69, 0.71),
                    new SearchDocumentSeed("note-2", "note", "A short note highlighting recall for vague project ideas and hidden context.", "The note overlaps with the search language used in the query.", null, new[] { "note" }, 0.62, 0.74),
                    new SearchDocumentSeed("website-2", "website", "The website page covers note-assisted search for long-form video and transcripts.", "The page contains similar language about hidden context and recall.", null, new[] { "website" }, 0.6, 0.7)
                })
        };
    }

    private sealed record SearchClusterSeed(
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
        IReadOnlyList<SearchSubmatchSeed> Submatches,
        IReadOnlyList<SearchRelatedItemSeed> RelatedItems,
        int RecentOpenCount,
        bool HasMatchingNote,
        IReadOnlyList<SearchDocumentSeed> Documents);

    private sealed record SearchDocumentSeed(
        string Id,
        string DocumentType,
        string Text,
        string? Snippet,
        string? Field,
        IReadOnlyList<string>? MatchedFields,
        double TextScore,
        double VectorScore);

    private sealed record SearchSubmatchSeed(string Title, string Type, string Detail, string? TimestampLabel, string? Url);

    private sealed record SearchRelatedItemSeed(string Title, string Type, double RelativeSimilarityPercent, string? Url, string? Detail);
}

public sealed class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public SearchFilters Filters { get; set; } = new();
    public SearchUiSettings? Settings { get; set; }
}

public sealed class SearchFilters
{
    public string? Channel { get; set; }
    public string? DateRange { get; set; }
    public string? ResultType { get; set; }
    public bool? HasTranscript { get; set; }
    public bool? HasRepo { get; set; }
    public bool? HasNotes { get; set; }
    public string? IngestionStatus { get; set; }

    public static SearchFilters Empty { get; } = new();
}

public sealed class SearchUiSettings
{
    public double TextWeight { get; set; } = 0.7;
    public double VectorWeight { get; set; } = 0.3;

    public static SearchUiSettings Default { get; } = new();
}

public sealed class SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public IReadOnlyList<SearchResultClusterResponse> Results { get; set; } = Array.Empty<SearchResultClusterResponse>();
    public IReadOnlyList<string> RecentSearches { get; set; } = Array.Empty<string>();
    public SearchUiSettings Settings { get; set; } = SearchUiSettings.Default;
    public string Summary { get; set; } = string.Empty;
}

public sealed class SearchResultClusterResponse
{
    public string ClusterId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset PublishDate { get; set; }
    public string ResultType { get; set; } = string.Empty;
    public bool HasTranscript { get; set; }
    public bool HasRepo { get; set; }
    public bool HasNotes { get; set; }
    public bool HasScreenshot { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public bool CanRetry { get; set; }
    public int MatchesInsideCount { get; set; }
    public string? PrimaryMatch { get; set; }
    public string? PrimaryMatchTimestamp { get; set; }
    public double Score { get; set; }
    public string ScoreExplanation { get; set; } = string.Empty;
    public double RelativeSimilarityPercent { get; set; }
    public IReadOnlyList<string> MatchedFields { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ProcessingWarnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RepositoryLinks { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WebsiteLinks { get; set; } = Array.Empty<string>();
    public string? ScreenshotUrl { get; set; }
    public IReadOnlyList<SearchSubmatchResponse> Submatches { get; set; } = Array.Empty<SearchSubmatchResponse>();
    public IReadOnlyList<SearchRelatedItemResponse> RelatedItems { get; set; } = Array.Empty<SearchRelatedItemResponse>();
    public SearchScoreComponentsResponse ScoreComponents { get; set; } = new();
}

public sealed class SearchSubmatchResponse
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? TimestampLabel { get; set; }
    public string? Url { get; set; }
}

public sealed class SearchRelatedItemResponse
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double RelativeSimilarityPercent { get; set; }
    public string? Url { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class SearchScoreComponentsResponse
{
    public double BaseScore { get; set; }
    public double MaxDocumentScore { get; set; }
    public double AverageTopThreeDocumentScore { get; set; }
    public double CoverageScore { get; set; }
    public double NoteBoost { get; set; }
    public double InteractionBoost { get; set; }
}
