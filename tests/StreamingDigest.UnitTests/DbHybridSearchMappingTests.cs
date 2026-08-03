using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class DbHybridSearchMappingTests
{
    [Fact]
    public async Task Db_search_groups_multiple_documents_from_the_same_video_into_one_cluster()
    {
        var videoId = Guid.NewGuid();
        var searcher = new FakeSearchCorpusSearcher(
            new SearchCorpusReadiness(true, 2),
            new SearchCorpusCluster(
                VideoId: videoId,
                ClusterId: videoId.ToString("D"),
                Title: "Building a hybrid search",
                Channel: "Search Channel",
                PublishDate: DateTimeOffset.UtcNow,
                ResultType: "video",
                HasTranscript: true,
                HasRepo: true,
                HasNotes: false,
                HasScreenshot: true,
                ProcessingStatus: "processed",
                CanRetry: false,
                Documents: new List<SearchCorpusDocument>
                {
                    new(Guid.NewGuid(), "video_metadata", 0.8, 0.6, "hybrid snippet", new[] { "title" }),
                    new(Guid.NewGuid(), "transcript_chunk", 0.5, 0.9, "another snippet", new[] { "body" })
                },
                RecentOpenCount: 0,
                HasMatchingNote: false));
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store, searcher, new FakeVideoClusterEmbeddingStore());

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "hybrid search",
            Filters = new SearchFilters { ResultType = "video" }
        });

        Assert.Single(response.Results);
        Assert.Equal(videoId, response.Results[0].VideoId);
        Assert.Equal(2, response.Results[0].MatchesInsideCount);
        Assert.NotEmpty(response.Results[0].ScoreExplanation);
        Assert.NotEmpty(response.Results[0].Submatches);
    }

    [Fact]
    public async Task Db_search_returns_a_waiting_state_when_the_corpus_is_empty()
    {
        var searcher = new FakeSearchCorpusSearcher(new SearchCorpusReadiness(false, 0));
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store, searcher, new FakeVideoClusterEmbeddingStore());

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "anything",
            Filters = new SearchFilters { ResultType = "video" }
        });

        Assert.Empty(response.Results);
        Assert.Contains("No searchable corpus yet", response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Db_search_changes_ordering_when_text_weight_dominates_vector_weight()
    {
        var firstVideo = Guid.NewGuid();
        var secondVideo = Guid.NewGuid();
        var clusters = new List<SearchCorpusCluster>
        {
            new(firstVideo, firstVideo.ToString("D"), "Vector winner", "Channel", DateTimeOffset.UtcNow, "video", true, false, false, false, "processed", false,
                new List<SearchCorpusDocument>
                {
                    new(Guid.NewGuid(), "video_metadata", 0.1, 0.95, "vector snippet", new[] { "title" })
                },
                0, false),
            new(secondVideo, secondVideo.ToString("D"), "Text winner", "Channel", DateTimeOffset.UtcNow, "video", true, false, false, false, "processed", false,
                new List<SearchCorpusDocument>
                {
                    new(Guid.NewGuid(), "video_metadata", 0.95, 0.1, "text snippet", new[] { "title" })
                },
                0, false)
        };
        var searcher = new FakeSearchCorpusSearcher(new SearchCorpusReadiness(true, 2), clusters.ToArray());
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store, searcher, new FakeVideoClusterEmbeddingStore());

        // Vector-heavy settings -> vector winner ranks first.
        service.UpdateSettings(new SearchUiSettings { TextWeight = 0.1, VectorWeight = 0.9 });
        var vectorHeavy = await service.SearchAsync(new SearchRequest { Query = "hybrid", Filters = new SearchFilters { ResultType = "video" } });
        Assert.Equal("Vector winner", vectorHeavy.Results[0].Title);

        // Text-heavy settings -> text winner ranks first.
        service.UpdateSettings(new SearchUiSettings { TextWeight = 0.9, VectorWeight = 0.1 });
        var textHeavy = await service.SearchAsync(new SearchRequest { Query = "hybrid", Filters = new SearchFilters { ResultType = "video" } });
        Assert.Equal("Text winner", textHeavy.Results[0].Title);
    }

    [Fact]
    public async Task Db_search_attaches_related_items_from_the_cluster_store()
    {
        var videoId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var searcher = new FakeSearchCorpusSearcher(
            new SearchCorpusReadiness(true, 1),
            new SearchCorpusCluster(
                videoId, videoId.ToString("D"), "Title", "Channel", DateTimeOffset.UtcNow, "video", true, false, false, false, "processed", false,
                new List<SearchCorpusDocument>
                {
                    new(Guid.NewGuid(), "video_metadata", 0.8, 0.7, "snippet", new[] { "title" })
                },
                0, false));
        var clusterStore = new FakeVideoClusterEmbeddingStore(new VideoClusterRelatedItem(relatedId, 90, 100, "test", "test", 3));
        var service = new SearchUiService(new InMemoryRecentSearchStore(), searcher, clusterStore);

        var response = await service.SearchAsync(new SearchRequest { Query = "hybrid", Filters = new SearchFilters { ResultType = "video" } });

        Assert.Single(response.Results);
        Assert.Single(response.Results[0].RelatedItems);
    }

    private sealed class FakeSearchCorpusSearcher : ISearchCorpusSearcher
    {
        private readonly SearchCorpusReadiness _readiness;
        private readonly IReadOnlyList<SearchCorpusCluster> _clusters;

        public FakeSearchCorpusSearcher(SearchCorpusReadiness readiness, params SearchCorpusCluster[] clusters)
        {
            _readiness = readiness;
            _clusters = clusters;
        }

        public Task<SearchCorpusReadiness> GetReadinessAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_readiness);

        public Task<IReadOnlyList<SearchCorpusCluster>> SearchAsync(SearchCorpusSearchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_clusters);
    }

    private sealed class FakeVideoClusterEmbeddingStore : IVideoClusterEmbeddingStore
    {
        private readonly IReadOnlyList<VideoClusterRelatedItem> _related;

        public FakeVideoClusterEmbeddingStore(params VideoClusterRelatedItem[] related)
        {
            _related = related;
        }

        public Task<IReadOnlyList<StoredVideoClusterEmbedding>> BuildForVideoAsync(Guid videoId, Guid? generatedByOperationId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkStaleForVideoAsync(Guid videoId, Guid? markedByOperationId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<VideoClusterHighSignalMatch>> GetHighSignalMatchesAsync(Guid videoId, double thresholdPercent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<VideoClusterRelatedItem>> GetRelatedVideosAsync(Guid videoId, int take = 3, CancellationToken cancellationToken = default)
            => Task.FromResult(_related);
    }

    private sealed class InMemoryRecentSearchStore : IRecentSearchStore
    {
        public List<string> StoredSearches { get; } = new();

        public Task<StoredRecentSearch> StoreSearchAsync(string query, SearchFilters filters, SearchUiSettings settings, CancellationToken cancellationToken = default)
        {
            StoredSearches.Add(query);
            return Task.FromResult(new StoredRecentSearch(Guid.NewGuid(), query, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<string>> ListRecentQueriesAsync(int take = 8, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(StoredSearches.AsReadOnly());

        public Task ClearRecentSearchesAsync(CancellationToken cancellationToken = default)
        {
            StoredSearches.Clear();
            return Task.CompletedTask;
        }

        public Task RecordInteractionAsync(SearchInteractionEvent interactionEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, int>> GetRecentOpenCountsAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

        public Task<StoredQueryEmbedding?> GetQueryEmbeddingAsync(Guid recentSearchId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoredQueryEmbedding?>(null);
    }
}
