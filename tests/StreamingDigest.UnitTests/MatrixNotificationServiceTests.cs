using System.Net;
using System.Text;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.UnitTests;

public sealed class MatrixNotificationServiceTests
{
    [Fact]
    public async Task SendDigestSummaryAsync_formats_digest_summary_and_sends_to_matrix()
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"body\":\"Streaming Digest manual run", body);
            Assert.Contains("1 new video", body);
            Assert.Contains("1 new resource", body);
            Assert.Contains("1 high-signal match", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"event_id\":\"$event\"}", Encoding.UTF8, "application/json")
            };
        });

        var options = new MatrixNotificationOptions
        {
            IsEnabled = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            AccessToken = "secret-token",
            RoomId = "!room:example.com"
        };

        var service = new MatrixNotificationService(new MatrixNotificationClient(new HttpClient(handler), options), options);
        var digest = new Digest(Guid.NewGuid(), "manual")
        {
            PayloadJson = DigestPayloadSerializer.Serialize(new DigestPayload
            {
                NewVideos = [new DigestItem { Label = "Video 1" }],
                NewResources = [new DigestResource { Name = "Resource 1" }],
                HighSignalMatches = [new HighSignalMatch { Label = "Match 1", SimilarityPercent = 90 }],
                FailedItems = [new DigestItem { Label = "Failed 1" }],
                SkippedItems = [new DigestItem { Label = "Skipped 1" }]
            })
        };

        var result = await service.SendDigestSummaryAsync(digest);

        Assert.True(result.Success);
        Assert.Equal("Matrix message sent.", result.Message);
        Assert.Contains("$event", result.ResponseBody);
    }

    [Fact]
    public async Task SendDigestSummaryAsync_skips_ineligible_run_types()
    {
        var options = new MatrixNotificationOptions
        {
            IsEnabled = true,
            OnManualRuns = false,
            OnScheduledRuns = true,
            HomeserverBaseUrl = "https://matrix.example.com",
            AccessToken = "secret-token",
            RoomId = "!room:example.com"
        };

        var service = new MatrixNotificationService(new MatrixNotificationClient(new HttpClient(), options), options);
        var digest = new Digest(Guid.NewGuid(), "manual")
        {
            PayloadJson = DigestPayloadSerializer.Serialize(new DigestPayload())
        };

        var result = await service.SendDigestSummaryAsync(digest);

        Assert.False(result.Success);
        Assert.Equal("Matrix notifications are disabled for this run type.", result.Message);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
