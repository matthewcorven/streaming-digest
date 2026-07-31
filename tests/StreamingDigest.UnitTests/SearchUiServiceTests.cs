using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchUiServiceTests
{
    [Fact]
    public async Task Search_ranks_clusters_with_the_active_settings_and_tracks_recent_queries()
    {
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store);
        service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0.2,
            VectorWeight = 0.8
        });

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "project idea search",
            Filters = new SearchFilters
            {
                ResultType = "video"
            }
        });

        Assert.Equal("project idea search", response.Query);
        Assert.NotEqual(Guid.Empty, response.RecentSearchId);
        Assert.NotEmpty(response.Results);
        Assert.Contains(response.RecentSearches, recent => string.Equals(recent, "project idea search", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Results, result => result.MatchesInsideCount > 0);
        Assert.Equal(0.2, response.Settings.TextWeight);
        Assert.Equal(0.8, response.Settings.VectorWeight);
        Assert.Single(store.StoredSearches);
    }

    [Fact]
    public async Task Search_groups_multiple_matches_from_the_same_video_into_one_cluster_and_uses_related_video_clusters()
    {
        var service = new SearchUiService(new InMemoryRecentSearchStore());

        var response = await service.SearchAsync(new SearchRequest
        {
            Query = "project idea search",
            Filters = new SearchFilters
            {
                ResultType = "video"
            }
        });

        var cluster = Assert.Single(response.Results, result => result.ClusterId == "cluster-search-ui");
        Assert.Equal("Designing a search-first video knowledge base", cluster.Title);
        Assert.Equal(12, cluster.MatchesInsideCount);
        Assert.Equal(1, cluster.Submatches.Count(match => string.Equals(match.Type, "segment", StringComparison.OrdinalIgnoreCase)));

        Assert.All(cluster.RelatedItems, related =>
        {
            Assert.InRange(related.RelativeSimilarityPercent, 0.0, 100.0);
        });

        Assert.Contains(cluster.RelatedItems, related => related.Title == "Repository README: search indexing");
        Assert.Contains(cluster.RelatedItems, related => related.Title == "Note: curation workflow");
        Assert.Contains(cluster.RelatedItems, related => related.Title == "Website page: ranking guidance");
    }

    [Fact]
    public async Task Search_moves_existing_recent_queries_to_the_end_without_dropping_other_entries()
    {
        var service = new SearchUiService(new InMemoryRecentSearchStore());

        await service.SearchAsync(new SearchRequest { Query = "alpha" });
        await service.SearchAsync(new SearchRequest { Query = "beta" });
        await service.SearchAsync(new SearchRequest { Query = "gamma" });
        await service.SearchAsync(new SearchRequest { Query = "beta" });

        Assert.Equal(new[] { "alpha", "gamma", "beta" }, await service.GetRecentSearchesAsync());
    }

    [Fact]
    public async Task Search_does_not_persist_request_scoped_settings()
    {
        var service = new SearchUiService(new InMemoryRecentSearchStore());
        service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0.2,
            VectorWeight = 0.8
        });

        await service.SearchAsync(new SearchRequest
        {
            Query = "alpha",
            Settings = new SearchUiSettings
            {
                TextWeight = 0.9,
                VectorWeight = 0.1
            }
        });

        Assert.Equal(0.2, service.GetSettings().TextWeight);
        Assert.Equal(0.8, service.GetSettings().VectorWeight);
    }

    [Fact]
    public async Task Clear_recent_searches_removes_history_but_keeps_interaction_events()
    {
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store);

        var response = await service.SearchAsync(new SearchRequest { Query = "alpha" });
        await service.RecordInteractionAsync(new SearchInteractionRequest
        {
            RecentSearchId = response.RecentSearchId,
            VideoId = response.Results[0].VideoId,
            ResultType = "video",
            EventType = "result_opened"
        });

        await service.ClearRecentSearchesAsync();

        Assert.Empty(await service.GetRecentSearchesAsync());
        Assert.Single(store.RecordedInteractions);
    }

    [Fact]
    public async Task Search_uses_recorded_interactions_to_increase_interaction_boost()
    {
        var store = new InMemoryRecentSearchStore();
        var service = new SearchUiService(store);

        var baseline = await service.SearchAsync(new SearchRequest { Query = "project idea search" });
        var topResult = baseline.Results[0];

        await service.RecordInteractionAsync(new SearchInteractionRequest
        {
            RecentSearchId = baseline.RecentSearchId,
            VideoId = topResult.VideoId,
            ResultType = topResult.ResultType,
            EventType = "result_opened"
        });

        var boosted = await service.SearchAsync(new SearchRequest { Query = "project idea search" });
        var boostedResult = boosted.Results.Single(result => result.VideoId == topResult.VideoId);

        Assert.True(boostedResult.ScoreComponents.InteractionBoost > topResult.ScoreComponents.InteractionBoost);
    }

    [Fact]
    public void UpdateSettings_rejects_non_positive_weight_sums()
    {
        var service = new SearchUiService(new InMemoryRecentSearchStore());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0,
            VectorWeight = 0
        }));

        Assert.Contains("positive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InMemoryRecentSearchStore : IRecentSearchStore
    {
        private readonly List<StoredRecentSearch> _storedSearches = [];
        private readonly List<SearchInteractionEvent> _recordedInteractions = [];

        public IReadOnlyList<StoredRecentSearch> StoredSearches => _storedSearches;
        public IReadOnlyList<SearchInteractionEvent> RecordedInteractions => _recordedInteractions;

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
            var counts = _recordedInteractions
                .Where(interaction => videoIds.Contains(interaction.VideoId))
                .GroupBy(interaction => interaction.VideoId)
                .ToDictionary(group => group.Key, group => group.Count());

            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(counts);
        }
    }
}
