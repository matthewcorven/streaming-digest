using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingDigest.Application.AudioToText;
using StreamingDigest.Infrastructure.AudioToText;

namespace StreamingDigest.UnitTests;

public sealed class LocalWhisperAudioToTextProviderTests
{
    [Fact]
    public async Task TranscribeAsync_sends_file_path_and_returns_mapped_result()
    {
        JsonElement? capturedRequest = null;
        string? requestedPath = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestedPath = request.RequestUri?.AbsolutePath;
            capturedRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            var response = new
            {
                engine = "whisper.cpp",
                model = "base.en",
                language = "en",
                durationSeconds = 600.0m,
                text = "Full transcript...",
                cues = new[]
                {
                    new { startSeconds = 0.0m, endSeconds = 4.2m, text = "Hello..." }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            };
        });

        var provider = CreateProvider(handler, new Uri("https://whisper.internal"));

        var result = await provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav", "en"), CancellationToken.None);

        Assert.Equal("/internal/audio-to-text/transcribe", requestedPath);
        Assert.True(capturedRequest.HasValue);
        Assert.Equal("/workspace/audio.wav", capturedRequest.Value.GetProperty("filePath").GetString());
        Assert.Equal("en", capturedRequest.Value.GetProperty("language").GetString());
        Assert.Equal("whisper.cpp", result.Engine);
        Assert.Equal("base.en", result.Model);
        Assert.Equal("en", result.Language);
        Assert.Equal(600.0m, result.DurationSeconds);
        Assert.Equal("Full transcript...", result.FullText);
        Assert.Single(result.Cues);
        Assert.Equal(0.0m, result.Cues[0].StartSeconds);
        Assert.Equal(4.2m, result.Cues[0].EndSeconds);
        Assert.Equal("Hello...", result.Cues[0].Text);
    }

    [Fact]
    public async Task TranscribeAsync_includes_language_hint_when_provided()
    {
        JsonElement? capturedRequest = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { engine = "whisper.cpp", model = "base", language = "fr", durationSeconds = 1.5m, text = "Bonjour", cues = Array.Empty<object>() })
            };
        });

        var provider = CreateProvider(handler, new Uri("https://whisper.internal"));

        await provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav", "fr"), CancellationToken.None);

        Assert.True(capturedRequest.HasValue);
        Assert.Equal("fr", capturedRequest.Value.GetProperty("language").GetString());
    }

    [Fact]
    public async Task TranscribeAsync_omits_language_when_not_provided()
    {
        JsonElement? capturedRequest = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { engine = "whisper.cpp", model = "base", language = (string?)null, durationSeconds = 1.5m, text = "Hello", cues = Array.Empty<object>() })
            };
        });

        var provider = CreateProvider(handler, new Uri("https://whisper.internal"));

        await provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav"), CancellationToken.None);

        Assert.True(capturedRequest.HasValue);
        Assert.True(capturedRequest.Value.TryGetProperty("language", out var languageProperty));
        Assert.Equal(JsonValueKind.Null, languageProperty.ValueKind);
    }

    [Fact]
    public async Task TranscribeAsync_returns_empty_result_when_base_address_not_configured()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP should not be called when unconfigured."));
        var provider = CreateProvider(handler, baseAddress: null);

        var result = await provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav", "en"), CancellationToken.None);

        Assert.Equal("whisper-unconfigured", result.Engine);
        Assert.Equal("none", result.Model);
        Assert.Equal("en", result.Language);
        Assert.Null(result.DurationSeconds);
        Assert.Equal(string.Empty, result.FullText);
        Assert.Empty(result.Cues);
    }

    [Fact]
    public async Task TranscribeAsync_throws_on_http_error()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var provider = CreateProvider(handler, new Uri("https://whisper.internal"));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav"), CancellationToken.None));
    }

    [Fact]
    public async Task TranscribeAsync_maps_empty_cues_list()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { engine = "whisper.cpp", model = "base", language = "en", durationSeconds = 2.0m, text = "Hello", cues = Array.Empty<object>() })
        }));

        var provider = CreateProvider(handler, new Uri("https://whisper.internal"));

        var result = await provider.TranscribeAsync(new AudioTranscriptionRequest("/workspace/audio.wav", "en"), CancellationToken.None);

        Assert.NotNull(result.Cues);
        Assert.Empty(result.Cues);
    }

    private static LocalWhisperAudioToTextProvider CreateProvider(HttpMessageHandler handler, Uri? baseAddress)
    {
        var httpClient = new HttpClient(handler);
        if (baseAddress is not null)
        {
            httpClient.BaseAddress = baseAddress;
        }

        return new LocalWhisperAudioToTextProvider(httpClient, NullLogger<LocalWhisperAudioToTextProvider>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
