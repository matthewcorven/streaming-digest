namespace StreamingDigest.Application;

public sealed class SearchUiService
{
    private readonly object _syncRoot = new();
    private readonly IRecentSearchStore _recentSearchStore;
    private readonly HybridRankingService? _injectedRankingService;
    private SearchUiSettings _settings = SearchUiSettings.Default;

    public SearchUiService(IRecentSearchStore recentSearchStore, HybridRankingService? rankingService = null)
    {
        _recentSearchStore = recentSearchStore ?? throw new ArgumentNullException(nameof(recentSearchStore));
        _injectedRankingService = rankingService;
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

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => SearchAsync(request, stateKey: null, cancellationToken);

    public async Task<SearchResponse> SearchAsync(SearchRequest request, string? stateKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedQuery = string.IsNullOrWhiteSpace(request.Query) ? "project idea search" : request.Query.Trim();
        var terms = ExtractTerms(normalizedQuery);
        var activeFilters = request.Filters ?? SearchFilters.Empty;
        var effectiveSettings = GetSettings();

        var candidateSeeds = CreateCandidateSeeds()
            .GroupBy(seed => seed.VideoId)
            .Select(MergeVideoCluster)
            .Where(seed => MatchesFilters(seed, activeFilters))
            .ToList();

        var storedSearch = await _recentSearchStore.StoreSearchAsync(
            normalizedQuery,
            activeFilters,
            effectiveSettings,
            cancellationToken);

        var recentSearches = await _recentSearchStore.ListRecentQueriesAsync(cancellationToken: cancellationToken);

        if (candidateSeeds.Count == 0)
        {
            return CreateEmptyResponse(normalizedQuery, storedSearch.Id, recentSearches, effectiveSettings);
        }

        var candidateSeedLookup = candidateSeeds.ToDictionary(seed => seed.Id, StringComparer.OrdinalIgnoreCase);
        var relatedItemsByClusterId = candidateSeeds.ToDictionary(
            seed => seed.Id,
            seed => (IReadOnlyList<SearchRelatedItemResponse>)BuildRelatedItems(seed, candidateSeeds),
            StringComparer.OrdinalIgnoreCase);

        var recentOpenCounts = await _recentSearchStore.GetRecentOpenCountsAsync(
            candidateSeeds.Select(seed => seed.VideoId),
            cancellationToken);

        var rankedClusters = candidateSeeds
            .Select(seed => CreateClusterCandidate(
                seed,
                normalizedQuery,
                terms,
                recentOpenCounts.TryGetValue(seed.VideoId, out var recentOpenCount) ? recentOpenCount : 0))
            .ToList();

        var rankingService = CreateRankingService(effectiveSettings);
        var rankingResults = rankingService.Rank(
            rankedClusters,
            relativeSimilarityPoolScores: rankedClusters.Select(cluster => cluster.Documents.Max(document => document.VectorScore)).ToList());

        var results = rankingResults
            .Select(result => MapResult(
                result,
                candidateSeedLookup[result.ClusterId],
                relatedItemsByClusterId[result.ClusterId]))
            .ToList();

        return new SearchResponse
        {
            Query = normalizedQuery,
            RecentSearchId = storedSearch.Id,
            Results = results,
            RecentSearches = recentSearches,
            Settings = effectiveSettings,
            Summary = results.Count == 0
                ? "No clusters matched the current filters."
                : $"Showing {results.Count} clustered video result{(results.Count == 1 ? string.Empty : "s")} for '{normalizedQuery}'."
        };
    }

    public Task<IReadOnlyList<string>> GetRecentSearchesAsync(CancellationToken cancellationToken = default)
        => GetRecentSearchesAsync(stateKey: null, cancellationToken);

    public Task<IReadOnlyList<string>> GetRecentSearchesAsync(string? stateKey, CancellationToken cancellationToken = default)
        => _recentSearchStore.ListRecentQueriesAsync(cancellationToken: cancellationToken);

    public Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default)
        => ClearRecentSearchesAsync(stateKey: null, cancellationToken);

    public Task ClearRecentSearchesAsync(string? stateKey, CancellationToken cancellationToken = default)
        => _recentSearchStore.ClearRecentSearchesAsync(cancellationToken);

    public void RemoveState(string? stateKey)
    {
    }

    public Task RecordInteractionAsync(SearchInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.VideoId == Guid.Empty)
        {
            throw new ArgumentException("A video id is required to record a search interaction.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ResultType))
        {
            throw new ArgumentException("A result type is required to record a search interaction.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            throw new ArgumentException("An event type is required to record a search interaction.", nameof(request));
        }

        return _recentSearchStore.RecordInteractionAsync(
            new SearchInteractionEvent(
                request.RecentSearchId,
                request.VideoId,
                request.SearchDocumentId,
                request.ResultType.Trim(),
                request.EventType.Trim(),
                request.MetadataJson,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static SearchResponse CreateEmptyResponse(
        string query,
        Guid recentSearchId,
        IReadOnlyList<string> recentSearches,
        SearchUiSettings settings)
    {
        return new SearchResponse
        {
            Query = query,
            RecentSearchId = recentSearchId,
            Results = Array.Empty<SearchResultClusterResponse>(),
            RecentSearches = recentSearches,
            Settings = settings,
            Summary = "No clusters matched the current filters."
        };
    }

    private HybridRankingService CreateRankingService(SearchUiSettings settings)
    {
        if (_injectedRankingService is not null)
        {
            return _injectedRankingService;
        }

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

    private static HybridClusterCandidate CreateClusterCandidate(
        SearchClusterSeed seed,
        string query,
        IReadOnlyList<string> terms,
        int recentOpenCount)
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
            Title: GetEffectiveTitle(seed),
            Documents: documents,
            RecentOpenCount: recentOpenCount,
            HasMatchingNote: seed.HasMatchingNote);
    }

    private static SearchResultClusterResponse MapResult(
        HybridClusterRankingResult ranking,
        SearchClusterSeed seed,
        IReadOnlyList<SearchRelatedItemResponse> relatedItems)
    {
        var primarySnippet = seed.Submatches.FirstOrDefault();
        return new SearchResultClusterResponse
        {
            ClusterId = ranking.ClusterId,
            VideoId = seed.VideoId,
            Title = GetEffectiveTitle(seed),
            Channel = seed.Channel,
            PublishDate = seed.PublishDate,
            ResultType = seed.ResultType,
            HasTranscript = seed.HasTranscript,
            HasRepo = seed.HasRepo,
            HasNotes = seed.HasNotes,
            HasScreenshot = !string.IsNullOrWhiteSpace(seed.ScreenshotUrl),
            ProcessingStatus = seed.ProcessingStatus,
            CanRetry = seed.CanRetry,
            MatchesInsideCount = seed.Submatches.Count,
            PrimaryMatch = primarySnippet?.Detail,
            PrimaryMatchTimestamp = primarySnippet?.TimestampLabel,
            PrimaryMatchUrl = primarySnippet?.Url,
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
            RelatedItems = relatedItems,
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

    private static SearchClusterSeed MergeVideoCluster(IGrouping<Guid, SearchClusterSeed> group)
    {
        var seeds = group.ToList();
        var canonicalSeed = seeds[0];
        var effectiveTitle = seeds
            .Select(seed => seed.TitleOverride)
            .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title));
        var originalTitle = seeds
            .Select(seed => seed.TitleOriginal)
            .First(title => !string.IsNullOrWhiteSpace(title));

        return new SearchClusterSeed(
            Id: canonicalSeed.Id,
            VideoId: canonicalSeed.VideoId,
            TitleOriginal: originalTitle,
            TitleOverride: effectiveTitle,
            Channel: canonicalSeed.Channel,
            PublishDate: canonicalSeed.PublishDate,
            ResultType: canonicalSeed.ResultType,
            HasTranscript: seeds.Any(seed => seed.HasTranscript),
            HasRepo: seeds.Any(seed => seed.HasRepo),
            HasNotes: seeds.Any(seed => seed.HasNotes),
            IngestionStatus: canonicalSeed.IngestionStatus,
            ProcessingStatus: canonicalSeed.ProcessingStatus,
            CanRetry: seeds.Any(seed => seed.CanRetry),
            ScreenshotUrl: seeds.Select(seed => seed.ScreenshotUrl).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
            RepositoryLinks: seeds.SelectMany(seed => seed.RepositoryLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WebsiteLinks: seeds.SelectMany(seed => seed.WebsiteLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ProcessingWarnings: seeds.SelectMany(seed => seed.ProcessingWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Submatches: seeds.SelectMany(seed => seed.Submatches).ToList(),
            RecentOpenCount: seeds.Max(seed => seed.RecentOpenCount),
            HasMatchingNote: seeds.Any(seed => seed.HasMatchingNote),
            Documents: seeds.SelectMany(seed => seed.Documents).ToList(),
            Fingerprint: AverageFingerprint(seeds.Select(seed => seed.Fingerprint).ToList()));
    }

    private static IReadOnlyList<SearchRelatedItemResponse> BuildRelatedItems(
        SearchClusterSeed seed,
        IReadOnlyList<SearchClusterSeed> corpus,
        int limit = 3)
    {
        var relatedCandidates = corpus
            .Where(candidate => candidate.VideoId != seed.VideoId)
            .Select(candidate => new
            {
                Seed = candidate,
                Similarity = CosineSimilarity(seed.Fingerprint, candidate.Fingerprint)
            })
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Seed.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (relatedCandidates.Count == 0)
        {
            return Array.Empty<SearchRelatedItemResponse>();
        }

        var similarityValues = relatedCandidates.Select(candidate => candidate.Similarity).ToList();

        return relatedCandidates
            .Take(limit)
            .Select(candidate => new SearchRelatedItemResponse
            {
                Title = GetEffectiveTitle(candidate.Seed),
                Type = candidate.Seed.ResultType,
                RelativeSimilarityPercent = Math.Round(
                    NormalizeRelativeSimilarity(candidate.Similarity, similarityValues),
                    2),
                Url = ChoosePrimaryLink(candidate.Seed),
                Detail = $"{candidate.Seed.Channel} · {candidate.Seed.Submatches.Count} related match{(candidate.Seed.Submatches.Count == 1 ? string.Empty : "es")} across the corpus."
            })
            .ToList();
    }

    private static string GetEffectiveTitle(SearchClusterSeed seed)
        => string.IsNullOrWhiteSpace(seed.TitleOverride) ? seed.TitleOriginal : seed.TitleOverride!;

    private static string? ChoosePrimaryLink(SearchClusterSeed seed)
        => seed.Submatches.Select(item => item.Url).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            ?? seed.RepositoryLinks.FirstOrDefault()
            ?? seed.WebsiteLinks.FirstOrDefault();

    private static double[] AverageFingerprint(IReadOnlyList<double[]> fingerprints)
    {
        if (fingerprints.Count == 0)
        {
            return new[] { 0d, 0d, 0d };
        }

        var dimensions = fingerprints[0].Length;
        var combined = new double[dimensions];
        foreach (var fingerprint in fingerprints)
        {
            for (var index = 0; index < dimensions; index++)
            {
                combined[index] += fingerprint[index];
            }
        }

        for (var index = 0; index < dimensions; index++)
        {
            combined[index] /= fingerprints.Count;
        }

        return combined;
    }

    private static double CosineSimilarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0d;
        }

        var dot = 0d;
        var leftMagnitude = 0d;
        var rightMagnitude = 0d;

        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static double NormalizeRelativeSimilarity(double score, IReadOnlyList<double> corpusScores)
    {
        if (corpusScores.Count == 0)
        {
            return 0d;
        }

        var min = corpusScores.Min();
        var max = corpusScores.Max();
        if (Math.Abs(max - min) < 0.0000001d)
        {
            return 100d;
        }

        return 100d * (score - min) / (max - min);
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
                VideoId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TitleOriginal: "Designing a search-first video knowledge base",
                TitleOverride: "Designing a search-first knowledge base",
                Channel: "Tonbis AI Garage",
                PublishDate: new DateTimeOffset(2024, 1, 12, 10, 30, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: true,
                HasNotes: true,
                IngestionStatus: "processed",
                ProcessingStatus: "processed",
                CanRetry: false,
                ScreenshotUrl: "/images/placeholder-thumbnail.png",
                RepositoryLinks: new[] { "https://github.com/matthewcorven/streaming-digest" },
                WebsiteLinks: new[] { "https://docs.streaming-digest.dev/search" },
                ProcessingWarnings: new[] { "Transcript snippet is partial while the model is still indexing the full transcript." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Search experience walkthrough", "segment", "The transcript mentions a project idea search flow and a repo link to the matching cluster.", "06:34", "/watch/cluster-search-ui#t=404"),
                    new SearchSubmatchSeed("Repository note boost", "note", "A note attached to the video adds an extra boost for search and curation workflows.", "10:06", "/notes/cluster-search-ui")
                },
                RecentOpenCount: 8,
                HasMatchingNote: true,
                Documents: new[]
                {
                    new SearchDocumentSeed("segment-1", "segment", "A practical walkthrough of a search-first workflow for project ideas and repository links.", "Project idea search surfaced a cluster with transcript, repo, and note evidence.", null, new[] { "title", "transcript" }, 0.95, 0.82),
                    new SearchDocumentSeed("segment-2", "transcript", "A video transcript section explaining how search results cluster by video and include timestamp links.", "The transcript highlights how notes and repository links reinforce the best match.", null, new[] { "transcript" }, 0.88, 0.76),
                    new SearchDocumentSeed("repo-1", "repository", "The repository README explains search indexing, ranking, and vector retrieval for the product.", "Repository search terms align with the query and the note content.", null, new[] { "repository" }, 0.84, 0.9)
                },
                Fingerprint: new[] { 0.92, 0.83, 0.79 }),
            new(
                Id: "cluster-search-ui-supporting",
                VideoId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TitleOriginal: "Designing a search-first video knowledge base",
                TitleOverride: null,
                Channel: "Tonbis AI Garage",
                PublishDate: new DateTimeOffset(2024, 1, 12, 10, 30, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: true,
                HasNotes: true,
                IngestionStatus: "processed",
                ProcessingStatus: "processed",
                CanRetry: false,
                ScreenshotUrl: "/images/placeholder-thumbnail.png",
                RepositoryLinks: new[] { "https://github.com/matthewcorven/streaming-digest" },
                WebsiteLinks: new[] { "https://docs.streaming-digest.dev/search" },
                ProcessingWarnings: Array.Empty<string>(),
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Website summary match", "website", "The website summary includes ranking weights and search result clustering guidance.", "12:12", "https://docs.streaming-digest.dev/search"),
                    new SearchSubmatchSeed("Cluster follow-up segment", "segment", "A later segment reinforces that multiple segment hits still roll up to one video cluster.", "14:45", "/watch/cluster-search-ui#t=885")
                },
                RecentOpenCount: 8,
                HasMatchingNote: true,
                Documents: new[]
                {
                    new SearchDocumentSeed("website-1", "website", "The website page explains why text and vector weighting should be tuned for vague queries.", "Website content overlaps on search, ranking, and recent-search behavior.", null, new[] { "website" }, 0.81, 0.86),
                    new SearchDocumentSeed("note-1", "note", "A note that captures the search curation experience and why user signals boost the right cluster.", "The note contains the user-facing language from the query.", null, new[] { "note" }, 0.79, 0.84)
                },
                Fingerprint: new[] { 0.9, 0.82, 0.81 }),
            new(
                Id: "cluster-ranking-weights",
                VideoId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TitleOriginal: "Balancing text and vector ranking weights",
                TitleOverride: null,
                Channel: "Microsoft Build",
                PublishDate: new DateTimeOffset(2024, 2, 22, 14, 20, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: false,
                HasNotes: false,
                IngestionStatus: "stale",
                ProcessingStatus: "stale",
                CanRetry: true,
                ScreenshotUrl: string.Empty,
                RepositoryLinks: Array.Empty<string>(),
                WebsiteLinks: new[] { "https://learn.microsoft.com/azure/search" },
                ProcessingWarnings: new[] { "The cluster is stale because the transcript was reprocessed after the last ranking pass." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Weight tuning walkthrough", "segment", "The segment compares lexical and vector ranking for undefined or vague prompts.", "12:04", "/watch/cluster-ranking-weights#t=724"),
                    new SearchSubmatchSeed("Similarity explanation", "segment", "The transcript describes why relative similarity is useful for cluster trust.", "19:11", "/watch/cluster-ranking-weights#t=1151")
                },
                RecentOpenCount: 4,
                HasMatchingNote: false,
                Documents: new[]
                {
                    new SearchDocumentSeed("rank-1", "segment", "A deep dive into text and vector weighting for vague prompts.", "The segment compares lexical and semantic matching strength.", null, new[] { "transcript" }, 0.76, 0.88),
                    new SearchDocumentSeed("rank-2", "transcript", "The transcript mentions relative similarity percentages and why they matter for trust.", "It is aligned with the ranking explanation in the query.", null, new[] { "transcript" }, 0.72, 0.79),
                    new SearchDocumentSeed("rank-3", "website", "A website article that covers hybrid search and scoring trade-offs.", "The article includes language about applying ranking weights to search results.", null, new[] { "website" }, 0.68, 0.84)
                },
                Fingerprint: new[] { 0.78, 0.88, 0.75 }),
            new(
                Id: "cluster-transcript-notes",
                VideoId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TitleOriginal: "Using notes and transcripts to recover hidden context",
                TitleOverride: null,
                Channel: "The Practical AI Channel",
                PublishDate: new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.Zero),
                ResultType: "video",
                HasTranscript: true,
                HasRepo: false,
                HasNotes: true,
                IngestionStatus: "failed",
                ProcessingStatus: "failed",
                CanRetry: true,
                ScreenshotUrl: string.Empty,
                RepositoryLinks: Array.Empty<string>(),
                WebsiteLinks: new[] { "https://example.com/notes-and-search" },
                ProcessingWarnings: new[] { "The embedding pass failed for the linked note, so the score is provisional." },
                Submatches: new[]
                {
                    new SearchSubmatchSeed("Note-assisted recall", "note", "The note improves recall when the transcript is sparse.", "03:22", "/notes/cluster-transcript-notes"),
                    new SearchSubmatchSeed("Transcript recovery example", "transcript", "The transcript shows how weak terms still recover the right cluster when notes are involved.", "08:18", "/watch/cluster-transcript-notes#t=498")
                },
                RecentOpenCount: 2,
                HasMatchingNote: true,
                Documents: new[]
                {
                    new SearchDocumentSeed("transcript-1", "transcript", "The video explains how notes and transcript context can recover latent ideas.", "The query over-indexes on note and transcript vocabulary.", null, new[] { "transcript" }, 0.69, 0.71),
                    new SearchDocumentSeed("note-2", "note", "A short note highlighting recall for vague project ideas and hidden context.", "The note overlaps with the search language used in the query.", null, new[] { "note" }, 0.62, 0.74),
                    new SearchDocumentSeed("website-2", "website", "The website page covers note-assisted search for long-form video and transcripts.", "The page contains similar language about hidden context and recall.", null, new[] { "website" }, 0.6, 0.7)
                },
                Fingerprint: new[] { 0.73, 0.69, 0.83 })
        };
    }

    private sealed record SearchClusterSeed(
        string Id,
        Guid VideoId,
        string TitleOriginal,
        string? TitleOverride,
        string Channel,
        DateTimeOffset PublishDate,
        string ResultType,
        bool HasTranscript,
        bool HasRepo,
        bool HasNotes,
        string IngestionStatus,
        string ProcessingStatus,
        bool CanRetry,
        string? ScreenshotUrl,
        IReadOnlyList<string> RepositoryLinks,
        IReadOnlyList<string> WebsiteLinks,
        IReadOnlyList<string> ProcessingWarnings,
        IReadOnlyList<SearchSubmatchSeed> Submatches,
        int RecentOpenCount,
        bool HasMatchingNote,
        IReadOnlyList<SearchDocumentSeed> Documents,
        double[] Fingerprint);

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
    public Guid RecentSearchId { get; set; }
    public IReadOnlyList<SearchResultClusterResponse> Results { get; set; } = Array.Empty<SearchResultClusterResponse>();
    public IReadOnlyList<string> RecentSearches { get; set; } = Array.Empty<string>();
    public SearchUiSettings Settings { get; set; } = SearchUiSettings.Default;
    public string Summary { get; set; } = string.Empty;
}

public sealed class SearchResultClusterResponse
{
    public string ClusterId { get; set; } = string.Empty;
    public Guid VideoId { get; set; }
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
    public string? PrimaryMatchUrl { get; set; }
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

public sealed class SearchInteractionRequest
{
    public Guid? RecentSearchId { get; set; }
    public Guid VideoId { get; set; }
    public Guid? SearchDocumentId { get; set; }
    public string ResultType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}
