using System.Net;
using System.Text;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.UnitTests;

public sealed class MatrixNotificationClientTests
{
    [Fact]
    public void BuildPreview_formats_message_for_matrix()
    {
        var video = new Video(Guid.NewGuid(), "Example video")
        {
            PlatformVideoUrl = "https://example.com/video",
            PlatformVideoId = "video-1",
            YoutubeVideoId = "video-1",
            AuthorOriginal = "Example author",
            VideoUrl = "https://example.com/video"
        };

        var client = new MatrixNotificationClient(new HttpClient(), new MatrixNotificationOptions
        {
            IsEnabled = false,
            HomeserverBaseUrl = "https://matrix.example.com"
        });

        var preview = client.BuildPreview(video, "new items found");

        Assert.Equal("[matrix] Example video: new items found", preview);
    }

    [Fact]
    public async Task SendTextMessageAsync_posts_unencrypted_message_to_matrix()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("Bearer secret-token", request.Headers.Authorization?.ToString());

            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"msgtype\":\"m.text\"", body);
            Assert.Contains("\"body\":\"hello from digest\"", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"event_id\":\"$event\"}", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var options = new MatrixNotificationOptions
        {
            IsEnabled = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            AccessToken = "secret-token",
            RoomId = "!room:example.com"
        };
        var client = new MatrixNotificationClient(httpClient, options);

        var result = await client.SendTextMessageAsync("hello from digest");

        Assert.True(result.Success);
        Assert.Equal("Matrix message sent.", result.Message);
        Assert.Contains("$event", result.ResponseBody);
        Assert.Equal("$event", result.ProviderMessageId);
        Assert.Contains("/_matrix/client/v3/rooms/!room%3Aexample.com/send/m.room.message/", handler.RequestUri!.ToString());
    }

    [Fact]
    public void Client_constructs_with_empty_values_when_disabled()
    {
        var client = new MatrixNotificationClient(new HttpClient(), new MatrixNotificationOptions
        {
            IsEnabled = false,
            HomeserverBaseUrl = "https://matrix.example.com"
        });

        var health = client.GetHealth();

        Assert.False(health.Enabled);
        Assert.Equal("disabled", health.Status);
        Assert.Equal("Matrix notifications are disabled.", health.Reason);
    }

    [Fact]
    public void Client_constructs_with_enabled_but_missing_token()
    {
        var client = new MatrixNotificationClient(new HttpClient(), new MatrixNotificationOptions
        {
            IsEnabled = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            RoomId = "!room:example.com"
        });

        var health = client.GetHealth();

        Assert.True(health.Enabled);
        Assert.Equal("needs-token", health.Status);
        Assert.Equal("Add a Matrix access token to start sending.", health.Reason);
    }

    [Fact]
    public void Client_constructs_with_enabled_but_missing_Room()
    {
        var client = new MatrixNotificationClient(new HttpClient(), new MatrixNotificationOptions
        {
            IsEnabled = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            AccessToken = "secret-token"
        });

        var health = client.GetHealth();

        Assert.True(health.Enabled);
        Assert.Equal("needs-room", health.Status);
        Assert.Equal("Add a Matrix room ID to start sending.", health.Reason);
    }

    [Fact]
    public void Client_constructs_with_enabled_and_complete_config()
    {
        var client = new MatrixNotificationClient(new HttpClient(), new MatrixNotificationOptions
        {
            IsEnabled = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            AccessToken = "secret-token",
            RoomId = "!room:example.com"
        });

        var health = client.GetHealth();

        Assert.True(health.Enabled);
        Assert.Equal("ready", health.Status);
        Assert.Equal("Matrix notifications are configured.", health.Reason);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return await handler(request);
        }
    }
}
