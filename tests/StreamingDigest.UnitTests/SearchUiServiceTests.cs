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
