using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StreamingDigest.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SearchUiApiSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SearchUiApiSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SearchUiEndpoints_require_authentication()
    {
        using var client = _factory.CreateClient();

        var settingsGet = await client.GetAsync("/api/search-ui/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, settingsGet.StatusCode);

        var settingsPost = await client.PostAsJsonAsync("/api/search-ui/settings", new { textWeight = 0.7, vectorWeight = 0.3 });
        Assert.Equal(HttpStatusCode.Unauthorized, settingsPost.StatusCode);

        var recentGet = await client.GetAsync("/api/search-ui/recent");
        Assert.Equal(HttpStatusCode.Unauthorized, recentGet.StatusCode);

        var recentDelete = await client.DeleteAsync("/api/search-ui/recent");
        Assert.Equal(HttpStatusCode.Unauthorized, recentDelete.StatusCode);

        var searchPost = await client.PostAsJsonAsync("/api/search-ui/search", new { query = "demo" });
        Assert.Equal(HttpStatusCode.Unauthorized, searchPost.StatusCode);

        var interactionPost = await client.PostAsJsonAsync("/api/search-ui/interactions", new { videoId = Guid.NewGuid(), resultType = "video", eventType = "result_opened" });
        Assert.Equal(HttpStatusCode.Unauthorized, interactionPost.StatusCode);
    }

}
