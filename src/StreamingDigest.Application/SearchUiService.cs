namespace StreamingDigest.Application;

public sealed class SearchUiService
{
    public const string CandidateScoringVersion = "seeded-heuristic-blend-v1";
    public const string DbCandidateScoringVersion = "db-hybrid-tsvector-pgvector-v1";

    private readonly object _syncRoot = new();
    private readonly IRecentSearchStore _recentSearchStore;
    private readonly IReadOnlyList<SearchCorpusClusterSeed> _candidateSeeds;
    private readonly HybridRankingService? _injectedRankingService;
    private readonly bool _useSeedRecentOpenCount;
    private readonly ISearchCorpusSearcher? _corpusSearcher;
    private readonly IVideoClusterEmbeddingStore? _videoClusterEmbeddingStore;
    private SearchUiSettings _settings = SearchUiSettings.Default;

    public SearchUiService(IRecentSearchStore recentSearchStore, HybridRankingService? rankingService = null)
        : this(recentSearchStore, SearchUiCorpusCatalog.CreateDefaultFixtureCorpus(), rankingService, useSeedRecentOpenCount: false)
    {
    }

    public SearchUiService(
        IRecentSearchStore recentSearchStore,
        IReadOnlyList<SearchCorpusClusterSeed> candidateSeeds,
        HybridRankingService? rankingService = null,
        bool useSeedRecentOpenCount = false)
    {
        _recentSearchStore = recentSearchStore ?? throw new ArgumentNullException(nameof(recentSearchStore));
        _candidateSeeds = candidateSeeds ?? throw new ArgumentNullException(nameof(candidateSeeds));
        _injectedRankingService = rankingService;
        _useSeedRecentOpenCount = useSeedRecentOpenCount;
    }

    /// <summary>
    /// DB-backed constructor used by production DI. Runs real hybrid text+vector search over
    /// the live search corpus and aggregates one cluster per video via
    /// <see cref="HybridRankingService"/>. Fixture seeds are not used.
    /// </summary>
    public SearchUiService(
        IRecentSearchStore recentSearchStore,
        ISearchCorpusSearcher corpusSearcher,
        IVideoClusterEmbeddingStore videoClusterEmbeddingStore,
        HybridRankingService? rankingService = null)
        : this(recentSearchStore, Array.Empty<SearchCorpusClusterSeed>(), rankingService, useSeedRecentOpenCount: false)
    {
        _corpusSearcher = corpusSearcher ?? throw new ArgumentNullException(nameof(corpusSearcher));
        _videoClusterEmbeddingStore = videoClusterEmbeddingStore ?? throw new ArgumentNullException(nameof(videoClusterEmbeddingStore));
    }

    public SearchUiService(IReadOnlyList<SearchCorpusClusterSeed> candidateSeeds)
        : this(new InMemoryRecentSearchStore(), candidateSeeds, useSeedRecentOpenCount: true)
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

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        => SearchAsync(request, stateKey: null, cancellationToken);

    public SearchResponse Search(SearchRequest request)
        => Search(request, stateKey: null);

    public SearchResponse Search(SearchRequest request, string? stateKey)
        => SearchAsync(request, stateKey, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<SearchResponse> SearchAsync(SearchRequest request, string? stateKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_corpusSearcher is not null && _videoClusterEmbeddingStore is not null)
        {
            return await SearchDbAsync(request, _corpusSearcher, _videoClusterEmbeddingStore, cancellationToken);
        }

        return await SearchFixtureAsync(request, cancellationToken);
    }

    private async Task<SearchResponse> SearchFixtureAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedQuery = string.IsNullOrWhiteSpace(request.Query) ? "project idea search" : request.Query.Trim();
        var terms = ExtractTerms(normalizedQuery);
        var activeFilters = request.Filters ?? SearchFilters.Empty;
        var effectiveSettings = GetSettings();

        var candidateSeeds = _candidateSeeds
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

        var recentOpenCounts = await _recentSearchStore.GetRecentOpenCountsAsync(
            candidateSeeds.Select(GetVideoId),
            cancellationToken);

        var rankedClusters = candidateSeeds
            .Select(seed => CreateClusterCandidate(
                seed,
                normalizedQuery,
                terms,
                (_useSeedRecentOpenCount ? seed.RecentOpenCount : 0)
                + (recentOpenCounts.TryGetValue(GetVideoId(seed), out var recentOpenCount) ? recentOpenCount : 0)))
            .ToList();

        var rankingService = CreateRankingService(effectiveSettings);
        var rankingResults = rankingService.Rank(
            rankedClusters,
            relativeSimilarityPoolScores: rankedClusters.Select(cluster => cluster.Documents.Max(document => document.VectorScore)).ToList());

        var results = rankingResults
            .Select(result => MapResult(
                result,
                candidateSeeds.First(seed => string.Equals(seed.Id, result.ClusterId, StringComparison.OrdinalIgnoreCase))))
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

    private async Task<SearchResponse> SearchDbAsync(
        SearchRequest request,
        ISearchCorpusSearcher corpusSearcher,
        IVideoClusterEmbeddingStore videoClusterEmbeddingStore,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(request.Query) ? string.Empty : request.Query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            throw new ArgumentException("A search query is required.", nameof(request));
        }

        var activeFilters = request.Filters ?? SearchFilters.Empty;
        var effectiveSettings = GetSettings();

        var readiness = await corpusSearcher.GetReadinessAsync(cancellationToken);
        if (!readiness.HasSearchableCorpus)
        {
            // Persist the recent search so the waiting state still tracks intent, but never
            // fabricate results. The query embedding is best-effort (degrades to text-only
            // when the embedding service is unavailable).
            var waitingStoredSearch = await _recentSearchStore.StoreSearchAsync(
                normalizedQuery,
                activeFilters,
                effectiveSettings,
                cancellationToken);
            var waitingRecent = await _recentSearchStore.ListRecentQueriesAsync(cancellationToken: cancellationToken);

            return new SearchResponse
            {
                Query = normalizedQuery,
                RecentSearchId = waitingStoredSearch.Id,
                Results = Array.Empty<SearchResultClusterResponse>(),
                RecentSearches = waitingRecent,
                Settings = effectiveSettings,
                Summary = "No searchable corpus yet. Run ingestion to populate search."
            };
        }

        var storedSearch = await _recentSearchStore.StoreSearchAsync(
            normalizedQuery,
            activeFilters,
            effectiveSettings,
            cancellationToken);

        var recentSearches = await _recentSearchStore.ListRecentQueriesAsync(cancellationToken: cancellationToken);

        var queryEmbedding = await _recentSearchStore.GetQueryEmbeddingAsync(storedSearch.Id, cancellationToken);

        var clusters = await corpusSearcher.SearchAsync(
            new SearchCorpusSearchRequest(
                Query: normalizedQuery,
                QueryEmbedding: queryEmbedding?.Values,
                QueryEmbeddingProvider: queryEmbedding?.Provider,
                QueryEmbeddingModel: queryEmbedding?.Model,
                QueryEmbeddingDimensions: queryEmbedding?.Dimensions,
                Filters: activeFilters,
                Settings: effectiveSettings),
            cancellationToken);

        if (clusters.Count == 0)
        {
            return CreateEmptyResponse(normalizedQuery, storedSearch.Id, recentSearches, effectiveSettings);
        }

        var candidates = clusters
            .Select(cluster => new HybridClusterCandidate(
                Id: cluster.ClusterId,
                Title: cluster.Title,
                Documents: cluster.Documents
                    .Select(document => new HybridDocumentCandidate(
                        Id: document.SearchDocumentId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                        DocumentType: document.DocumentType,
                        TextScore: document.TextScore,
                        VectorScore: document.VectorScore,
                        MatchedFields: document.MatchedFields,
                        Snippet: document.Snippet))
                    .ToList(),
                RecentOpenCount: cluster.RecentOpenCount,
                HasMatchingNote: cluster.HasMatchingNote))
            .ToList();

        var rankingService = CreateRankingService(effectiveSettings);
        var rankingResults = rankingService.Rank(
            candidates,
            relativeSimilarityPoolScores: candidates.Select(cluster => cluster.Documents.Max(document => document.VectorScore)).ToList());

        var clusterById = clusters.ToDictionary(cluster => cluster.ClusterId, StringComparer.OrdinalIgnoreCase);
        var results = new List<SearchResultClusterResponse>();
        foreach (var ranking in rankingResults)
        {
            if (!clusterById.TryGetValue(ranking.ClusterId, out var cluster))
            {
                continue;
            }

            var relatedItems = await BuildRelatedItemsAsync(videoClusterEmbeddingStore, cluster.VideoId, cancellationToken);
            results.Add(MapDbResult(ranking, cluster, relatedItems));
        }

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

    private static async Task<IReadOnlyList<SearchRelatedItemResponse>> BuildRelatedItemsAsync(
        IVideoClusterEmbeddingStore videoClusterEmbeddingStore,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        try
        {
            var related = await videoClusterEmbeddingStore.GetRelatedVideosAsync(videoId, take: 3, cancellationToken);
            return related
                .Select(item => new SearchRelatedItemResponse
                {
                    Title = item.VideoId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                    Type = "video",
                    RelativeSimilarityPercent = item.RelativeSimilarityPercent,
                    Detail = $"Related video (similarity {item.SimilarityPercent:0.##}%)"
                })
                .ToList();
        }
        catch (Exception)
        {
            // Related items are a progressive enhancement; never fail a search because the
            // cluster embedding store is unavailable.
            return Array.Empty<SearchRelatedItemResponse>();
        }
    }

    private static SearchResultClusterResponse MapDbResult(
        HybridClusterRankingResult ranking,
        SearchCorpusCluster cluster,
        IReadOnlyList<SearchRelatedItemResponse> relatedItems)
    {
        var primaryDocument = cluster.Documents
            .OrderByDescending(document => Math.Max(document.TextScore, document.VectorScore))
            .FirstOrDefault();

        var matchedFields = ranking.MatchedFields.Count > 0
            ? ranking.MatchedFields
            : (cluster.Documents
                .SelectMany(document => document.MatchedFields ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        return new SearchResultClusterResponse
        {
            ClusterId = ranking.ClusterId,
            VideoId = cluster.VideoId,
            Title = cluster.Title,
            Channel = cluster.Channel,
            PublishDate = cluster.PublishDate ?? DateTimeOffset.MinValue,
            ResultType = cluster.ResultType,
            HasTranscript = cluster.HasTranscript,
            HasRepo = cluster.HasRepo,
            HasNotes = cluster.HasNotes,
            HasScreenshot = cluster.HasScreenshot,
            ProcessingStatus = cluster.ProcessingStatus,
            CanRetry = cluster.CanRetry,
            MatchesInsideCount = cluster.Documents.Count,
            PrimaryMatch = primaryDocument?.Snippet,
            PrimaryMatchTimestamp = null,
            PrimaryMatchUrl = null,
            Score = ranking.Score,
            ScoreExplanation = ranking.Explanation,
            RelativeSimilarityPercent = ranking.RelativeSimilarityPercent,
            MatchedFields = matchedFields,
            ProcessingWarnings = Array.Empty<string>(),
            RepositoryLinks = Array.Empty<string>(),
            WebsiteLinks = Array.Empty<string>(),
            ScreenshotUrl = null,
            Submatches = cluster.Documents
                .Where(document => !string.IsNullOrWhiteSpace(document.Snippet))
                .Select(document => new SearchSubmatchResponse
                {
                    Title = document.DocumentType,
                    Type = document.DocumentType,
                    Detail = document.Snippet ?? string.Empty,
                    TimestampLabel = null,
                    Url = null
                })
                .ToList(),
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

    private static bool MatchesFilters(SearchCorpusClusterSeed seed, SearchFilters filters)
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
        SearchCorpusClusterSeed seed,
        string query,
        IReadOnlyList<string> terms,
        int recentOpenCount)
    {
        var documents = seed.Documents.Select(document =>
        {
            var textScore = BlendDocumentScore(document.TextScore, ScoreTextMatch(query, terms, document.Text, document.Snippet));
            var vectorScore = BlendDocumentScore(document.VectorScore, ScoreVectorMatch(query, terms, document.Text, document.Snippet));
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
            RecentOpenCount: recentOpenCount,
            HasMatchingNote: seed.HasMatchingNote);
    }

    private static SearchResultClusterResponse MapResult(
        HybridClusterRankingResult ranking,
        SearchCorpusClusterSeed seed)
    {
        var primarySnippet = seed.Submatches.FirstOrDefault();
        return new SearchResultClusterResponse
        {
            ClusterId = ranking.ClusterId,
            VideoId = GetVideoId(seed),
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
            MatchesInsideCount = seed.MatchesInsideCount > 0 ? seed.MatchesInsideCount : seed.Submatches.Count,
            PrimaryMatch = seed.PrimaryMatch ?? primarySnippet?.Detail,
            PrimaryMatchTimestamp = seed.PrimaryMatchTimestamp ?? primarySnippet?.TimestampLabel,
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
            RelatedItems = seed.RelatedItems.Select(item => new SearchRelatedItemResponse
            {
                Title = item.Title,
                Type = item.Type,
                Url = item.Url,
                RelativeSimilarityPercent = item.RelativeSimilarityPercent,
                Detail = item.Detail ?? string.Empty
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

    private static Guid GetVideoId(SearchCorpusClusterSeed seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"search-ui::{seed.Id}"));
        var guidBytes = bytes[..16].ToArray();
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static double BlendDocumentScore(double seededScore, double heuristicScore)
    {
        var normalizedSeed = ClampScore(seededScore);
        var normalizedHeuristic = ClampScore(heuristicScore);
        return Math.Round((0.6 * normalizedSeed) + (0.4 * normalizedHeuristic), 4);
    }

    private static double ClampScore(double score)
    {
        if (!double.IsFinite(score))
        {
            return 0.0;
        }

        return Math.Max(0.0, Math.Min(1.0, score));
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

    private sealed class InMemoryRecentSearchStore : IRecentSearchStore
    {
        private readonly List<StoredRecentSearch> _storedSearches = [];
        private readonly List<SearchInteractionEvent> _recordedInteractions = [];

        public Task<StoredRecentSearch> StoreSearchAsync(string query, SearchFilters filters, SearchUiSettings settings, CancellationToken cancellationToken = default)
        {
            var stored = new StoredRecentSearch(Guid.NewGuid(), query.Trim(), DateTimeOffset.UtcNow);
            _storedSearches.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<IReadOnlyList<string>> ListRecentQueriesAsync(int take = 8, CancellationToken cancellationToken = default)
        {
            var recentQueries = new List<string>();
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var query in _storedSearches.Select(item => item.QueryText).Reverse())
            {
                if (recentQueries.Count >= take || !observed.Add(query))
                {
                    continue;
                }

                recentQueries.Add(query);
            }

            recentQueries.Reverse();
            return Task.FromResult<IReadOnlyList<string>>(recentQueries);
        }

        public Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default)
        {
            _storedSearches.Clear();
            return Task.CompletedTask;
        }

        public Task RecordInteractionAsync(SearchInteractionEvent interaction, CancellationToken cancellationToken = default)
        {
            _recordedInteractions.Add(interaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, int>> GetRecentOpenCountsAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
        {
            var videoIdSet = videoIds.ToHashSet();
            var counts = _recordedInteractions
                .Where(interaction => videoIdSet.Contains(interaction.VideoId))
                .GroupBy(interaction => interaction.VideoId)
                .ToDictionary(group => group.Key, group => group.Count());

            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(counts);
        }

        public Task<StoredQueryEmbedding?> GetQueryEmbeddingAsync(Guid recentSearchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StoredQueryEmbedding?>(null);
        }
    }
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
