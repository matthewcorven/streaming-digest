using System.Net;
using System.Net.Http;
using System.Text;
using StreamingDigest.Application;
using StreamingDigest.UnitTests.Fixtures;

namespace StreamingDigest.UnitTests;

public sealed class YouTubeApiMetadataAdapterTests
{
    private readonly FixtureLoader _fixtures = new();

    // ── IsConfigured ──────────────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_returns_false_when_api_key_is_null()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public void IsConfigured_returns_false_when_api_key_is_whitespace()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), "   ");
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public void IsConfigured_returns_true_when_api_key_is_present()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), "fixture-api-key");
        Assert.True(adapter.IsConfigured);
    }

    // ── FetchChannelAsync — no API key ────────────────────────────────────────

    [Fact]
    public async Task FetchChannelAsync_returns_api_key_missing_when_not_configured()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);

        var result = await adapter.FetchChannelAsync("UC_some_channel");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsApiKeyMissing);
        Assert.Null(result.Channel);
    }

    // ── FetchChannelAsync — successful response ───────────────────────────────

    [Fact]
    public async Task FetchChannelAsync_returns_channel_from_api_response()
    {
        var body = _fixtures.ReadText("youtube-api/channel-response.json");
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("www.googleapis.com", request.RequestUri?.Host);
            Assert.Contains("channels", request.RequestUri?.AbsolutePath ?? string.Empty);
            Assert.Contains("UC_fixture_yt_api_channel", request.RequestUri?.Query ?? string.Empty);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchChannelAsync("UC_fixture_yt_api_channel");

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Channel fetch unexpectedly failed.");
        Assert.NotNull(result.Channel);
        Assert.Equal("UC_fixture_yt_api_channel", result.Channel!.YoutubeChannelId);
        Assert.Equal("Fixture API Channel", result.Channel.NameOriginal);
        Assert.Equal("Synthetic channel metadata returned by the YouTube Data API fixture.", result.Channel.DescriptionOriginal);
        Assert.Contains("UC_fixture_yt_api_channel", result.Channel.ProfileUrl);
        Assert.Contains("UC_fixture_yt_api_channel", result.Channel.SourceUrl);
    }

    // ── FetchChannelAsync — empty items list ──────────────────────────────────

    [Fact]
    public async Task FetchChannelAsync_returns_failure_when_channel_not_found()
    {
        const string emptyResponse = "{\"kind\":\"youtube#channelListResponse\",\"items\":[]}";
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyResponse, Encoding.UTF8, "application/json")
            });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchChannelAsync("UC_nonexistent");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Channel);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── FetchChannelAsync — rate limit ────────────────────────────────────────

    [Fact]
    public async Task FetchChannelAsync_returns_rate_limited_on_429()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchChannelAsync("UC_some_channel");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRateLimited);
        Assert.Equal(429, result.StatusCode);
    }

    // ── FetchVideoAsync — no API key ──────────────────────────────────────────

    [Fact]
    public async Task FetchVideoAsync_returns_api_key_missing_when_not_configured()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);

        var result = await adapter.FetchVideoAsync("some-video-id");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsApiKeyMissing);
        Assert.Null(result.Video);
    }

    // ── FetchVideoAsync — successful response ─────────────────────────────────

    [Fact]
    public async Task FetchVideoAsync_returns_video_from_api_response()
    {
        var body = _fixtures.ReadText("youtube-api/video-response.json");
        var channelId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("videos", request.RequestUri?.AbsolutePath ?? string.Empty);
            Assert.Contains("fixture-yt-api-video-id", request.RequestUri?.Query ?? string.Empty);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchVideoAsync("fixture-yt-api-video-id", channelId);

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Video fetch unexpectedly failed.");
        Assert.NotNull(result.Video);
        Assert.Equal("fixture-yt-api-video-id", result.Video!.YoutubeVideoId);
        Assert.Equal("Fixture API Video", result.Video.Title);
        Assert.Equal("Fixture API Channel", result.Video.AuthorOriginal);
        Assert.Equal("Synthetic video metadata returned by the YouTube Data API fixture.", result.Video.DescriptionOriginal);
        Assert.Equal(channelId, result.Video.ChannelId);
        Assert.Equal(630, result.Video.DurationSeconds); // PT10M30S = 10*60+30 = 630s
        Assert.NotNull(result.Video.PublishedAt);
        Assert.Equal(2026, result.Video.PublishedAt!.Value.Year);
        Assert.Contains("fixture-yt-api-video-id", result.Video.VideoUrl);
    }

    // ── FetchVideoAsync — missing video ───────────────────────────────────────

    [Fact]
    public async Task FetchVideoAsync_returns_failure_when_video_not_found()
    {
        const string emptyResponse = "{\"kind\":\"youtube#videoListResponse\",\"items\":[]}";
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyResponse, Encoding.UTF8, "application/json")
            });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchVideoAsync("nonexistent-video-id");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Video);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── FetchVideoAsync — rate limit ──────────────────────────────────────────

    [Fact]
    public async Task FetchVideoAsync_returns_rate_limited_on_429()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.FetchVideoAsync("some-video-id");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRateLimited);
        Assert.Equal(429, result.StatusCode);
    }

    // ── ListChannelVideoIdsAsync — no API key ─────────────────────────────────

    [Fact]
    public async Task ListChannelVideoIdsAsync_returns_api_key_missing_when_not_configured()
    {
        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(), null);

        var result = await adapter.ListChannelVideoIdsAsync("UC_some_channel");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsApiKeyMissing);
        Assert.Empty(result.VideoIds);
    }

    // ── ListChannelVideoIdsAsync — successful response ────────────────────────

    [Fact]
    public async Task ListChannelVideoIdsAsync_returns_video_ids_from_search_response()
    {
        var body = _fixtures.ReadText("youtube-api/search-response.json");
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("search", request.RequestUri?.AbsolutePath ?? string.Empty);
            Assert.Contains("UC_fixture_yt_api_channel", request.RequestUri?.Query ?? string.Empty);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.ListChannelVideoIdsAsync("UC_fixture_yt_api_channel", maxResults: 10);

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Search unexpectedly failed.");
        Assert.Equal(3, result.VideoIds.Count);
        Assert.Contains("fixture-video-id-1", result.VideoIds);
        Assert.Contains("fixture-video-id-2", result.VideoIds);
        Assert.Contains("fixture-video-id-3", result.VideoIds);
    }

    // ── ListChannelVideoIdsAsync — rate limit ─────────────────────────────────

    [Fact]
    public async Task ListChannelVideoIdsAsync_returns_rate_limited_on_429()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");

        var result = await adapter.ListChannelVideoIdsAsync("UC_some_channel");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRateLimited);
        Assert.Equal(429, result.StatusCode);
        Assert.Empty(result.VideoIds);
    }

    // ── ListChannelVideoIdsAsync — publishedAfter filter ─────────────────────

    [Fact]
    public async Task ListChannelVideoIdsAsync_includes_publishedAfter_in_query()
    {
        string? capturedQuery = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedQuery = request.RequestUri?.Query;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
            };
        });

        var adapter = new YouTubeApiMetadataAdapter(new HttpClient(handler), "fixture-api-key");
        var cutoff = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await adapter.ListChannelVideoIdsAsync("UC_some_channel", publishedAfter: cutoff);

        Assert.NotNull(capturedQuery);
        Assert.Contains("publishedAfter", capturedQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-01-01", capturedQuery);
    }

    // ── ParseIso8601Duration ──────────────────────────────────────────────────

    [Theory]
    [InlineData("PT10M30S", 630)]
    [InlineData("PT1H", 3600)]
    [InlineData("PT1H30M", 5400)]
    [InlineData("PT1H30M45S", 5445)]
    [InlineData("PT45S", 45)]
    [InlineData("PT0S", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("invalid", null)]
    public void ParseIso8601Duration_returns_expected_seconds(string? input, int? expected)
    {
        var result = YouTubeApiMetadataAdapter.ParseIso8601Duration(input);
        Assert.Equal(expected, result);
    }

    // ── Stub helper ───────────────────────────────────────────────────────────

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
