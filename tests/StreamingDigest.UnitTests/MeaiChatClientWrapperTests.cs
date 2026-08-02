using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public class MeaiChatClientWrapperTests
{
    [Fact]
    public async Task SendChatAsync_ReturnsResponseText()
    {
        // Arrange
        const string responseText = "This is the assistant's response.";
        var handler = new MockHttpMessageHandler(responseText);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        var result = await wrapper.SendChatAsync(
            "You are a helpful assistant.",
            "What is 2+2?",
            "llama3.2");

        // Assert
        Assert.Equal(responseText, result);
    }

    [Fact]
    public async Task SendChatAsync_ExtractsNestedJsonResponse()
    {
        // Arrange
        const string nestedJson = """{"message":{"content":"nested response"}}""";
        var handler = new MockHttpMessageHandler(nestedJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        var result = await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("nested response", result);
    }

    [Fact]
    public async Task SendChatAsync_ReturnsNullOnHttpFailure()
    {
        // Arrange
        var handler = new FailingHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        var result = await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SendChatAsync_ReturnsNullOnJsonParseException()
    {
        // Arrange
        const string invalidJson = "not json at all {{{";
        var handler = new MockHttpMessageHandler(invalidJson);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        var result = await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SendChatAsync_RespondsToRespects Temperature()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model",
            temperature: 0.7);

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains("0.7", requestBody);
    }

    [Fact]
    public async Task SendChatAsync_IncludesSystemPrompt()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        const string systemPrompt = "You are a JSON generator.";
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            systemPrompt,
            "User prompt",
            "model");

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains(systemPrompt, requestBody);
    }

    [Fact]
    public async Task SendChatAsync_IncludesUserPrompt()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        const string userPrompt = "Classify this URL: https://example.com";
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            "System prompt",
            userPrompt,
            "model");

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains(userPrompt, requestBody);
    }

    [Fact]
    public async Task SendChatAsync_IncludesModelName()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        const string modelName = "custom-llm-v2";
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            modelName);

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains(modelName, requestBody);
    }

    [Fact]
    public async Task SendChatAsync_SetsFormatToJson()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model");

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains("""format":"json\"""", requestBody);
    }

    [Fact]
    public async Task SendChatAsync_DisablesStreaming()
    {
        // Arrange
        var capturedRequest = new HttpRequestMessage();
        var handler = new CaptureHttpMessageHandler(capturedRequest, """{"message":{"content":"response"}}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var wrapper = new MeaiChatClientWrapper(httpClient);

        // Act
        await wrapper.SendChatAsync(
            "System prompt",
            "User prompt",
            "model");

        // Assert
        var requestBody = capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.Contains("""stream":false""", requestBody);
    }

    [Fact]
    public async Task TryExtractJsonField_ReturnsValueForSimplePath()
    {
        // Arrange
        const string json = """{"field":"value"}""";

        // Act
        var result = MeaiChatClientWrapper.TryExtractJsonField(json, "field", out var value);

        // Assert
        Assert.True(result);
        Assert.Equal("value", value);
    }

    [Fact]
    public async Task TryExtractJsonField_ReturnsValueForNestedPath()
    {
        // Arrange
        const string json = """{"outer":{"inner":"nested-value"}}""";

        // Act
        var result = MeaiChatClientWrapper.TryExtractJsonField(json, "outer.inner", out var value);

        // Assert
        Assert.True(result);
        Assert.Equal("nested-value", value);
    }

    [Fact]
    public async Task TryExtractJsonField_ReturnsFalseForMissingField()
    {
        // Arrange
        const string json = """{"field":"value"}""";

        // Act
        var result = MeaiChatClientWrapper.TryExtractJsonField(json, "missing", out var value);

        // Assert
        Assert.False(result);
        Assert.Null(value);
    }

    [Fact]
    public async Task TryExtractJsonField_HandlesInvalidJson()
    {
        // Arrange
        const string json = "not json {{{";

        // Act
        var result = MeaiChatClientWrapper.TryExtractJsonField(json, "field", out var value);

        // Assert
        Assert.False(result);
        Assert.Null(value);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public MockHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpRequestMessage _captureTarget;
        private readonly string _responseContent;

        public CaptureHttpMessageHandler(HttpRequestMessage captureTarget, string responseContent)
        {
            _captureTarget = captureTarget;
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _captureTarget.Method = request.Method;
            _captureTarget.RequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                _captureTarget.Content = new StringContent(
                    await request.Content.ReadAsStringAsync(cancellationToken),
                    Encoding.UTF8,
                    request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
        }
    }
}
