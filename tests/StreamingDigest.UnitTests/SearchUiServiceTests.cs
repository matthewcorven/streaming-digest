using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class SearchUiServiceTests
{
    [Fact]
    public void Search_ranks_clusters_with_the_active_settings_and_tracks_recent_queries()
    {
        var service = new SearchUiService();
        service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0.2,
            VectorWeight = 0.8
        });

        var response = service.Search(new SearchRequest
        {
            Query = "project idea search",
            Filters = new SearchFilters
            {
                ResultType = "video"
            }
        });

        Assert.Equal("project idea search", response.Query);
        Assert.NotEmpty(response.Results);
        Assert.Contains(response.RecentSearches, recent => string.Equals(recent, "project idea search", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Results, result => result.MatchesInsideCount > 0);
        Assert.Equal(0.2, response.Settings.TextWeight);
        Assert.Equal(0.8, response.Settings.VectorWeight);
    }

    [Fact]
    public void Search_moves_existing_recent_queries_to_the_end_without_dropping_other_entries()
    {
        var service = new SearchUiService();

        service.Search(new SearchRequest { Query = "alpha" });
        service.Search(new SearchRequest { Query = "beta" });
        service.Search(new SearchRequest { Query = "gamma" });
        service.Search(new SearchRequest { Query = "beta" });

        Assert.Equal(new[] { "alpha", "gamma", "beta" }, service.GetRecentSearches());
    }

    [Fact]
    public void Search_does_not_persist_request_scoped_settings()
    {
        var service = new SearchUiService();
        service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0.2,
            VectorWeight = 0.8
        });

        service.Search(new SearchRequest
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
    public void Recent_searches_are_isolated_by_state_key_while_settings_remain_global()
    {
        var service = new SearchUiService();

        service.Search(new SearchRequest { Query = "alpha" }, stateKey: "user-a");
        service.Search(new SearchRequest { Query = "beta" }, stateKey: "user-b");

        Assert.Equal(new[] { "alpha" }, service.GetRecentSearches("user-a"));
        Assert.Equal(new[] { "beta" }, service.GetRecentSearches("user-b"));

        service.UpdateSettings(new SearchUiSettings { TextWeight = 0.2, VectorWeight = 0.8 });

        Assert.Equal(0.2, service.GetSettings().TextWeight);
        Assert.Equal(0.8, service.GetSettings().VectorWeight);
    }

    [Fact]
    public void RemoveState_clears_the_recent_search_history_for_that_state_key()
    {
        var service = new SearchUiService();

        service.Search(new SearchRequest { Query = "alpha" }, stateKey: "user-a");
        service.Search(new SearchRequest { Query = "beta" }, stateKey: "user-b");

        service.RemoveState("user-a");

        Assert.Empty(service.GetRecentSearches("user-a"));
        Assert.Equal(new[] { "beta" }, service.GetRecentSearches("user-b"));
    }

    [Fact]
    public void UpdateSettings_rejects_non_positive_weight_sums()
    {
        var service = new SearchUiService();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateSettings(new SearchUiSettings
        {
            TextWeight = 0,
            VectorWeight = 0
        }));

        Assert.Contains("positive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
