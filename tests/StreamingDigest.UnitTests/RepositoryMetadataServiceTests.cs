using System.Net;
using System.Net.Http;
using System.Text;
using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class RepositoryMetadataServiceTests
{
    [Fact]
    public void Fetch_returns_deserialized_metadata_for_github_repositories()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("streaming-digest", request.Headers.UserAgent.ToString());

            if (request.RequestUri?.Host == "deepwiki.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><h1>DeepWiki fixture</h1><p>reachable page</p></body></html>", Encoding.UTF8, "text/html")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/readme") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"content\":\"IyBNaW5lIE5vdGUgUkVBRE1F\",\"encoding\":\"base64\"}", Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/license") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"content\":\"TUlU\",\"encoding\":\"base64\"}", Encoding.UTF8, "application/json")
                };
            }

            Assert.Contains("api.github.com/repos/matthewcorven/streaming-digest", request.RequestUri?.ToString() ?? string.Empty);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"description\":\"Streaming Digest\",\"stargazers_count\":42,\"language\":\"C#\",\"default_branch\":\"main\",\"private\":false,\"license\":{\"spdx_id\":\"MIT\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), httpClient);

        var result = service.Fetch("https://github.com/matthewcorven/streaming-digest");

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Repository metadata fetch unexpectedly failed.");
        Assert.NotNull(result.Metadata);
        Assert.Equal("https://github.com/matthewcorven/streaming-digest", result.Metadata!.CanonicalUrl);
        Assert.Equal("matthewcorven", result.Metadata.Owner);
        Assert.Equal("streaming-digest", result.Metadata.RepositoryName);
        Assert.Equal("Streaming Digest", result.Metadata.Description);
        Assert.Equal(42, result.Metadata.Stars);
        Assert.Equal("C#", result.Metadata.Language);
        Assert.Equal("main", result.Metadata.DefaultBranch);
        Assert.True(result.Metadata.IsPublic);
        Assert.Equal("MIT", result.Metadata.LicenseName);
        Assert.Equal("# Mine Note README", result.Metadata.ReadmeContent);
        Assert.Equal("MIT", result.Metadata.LicenseContent);
        Assert.Equal("https://deepwiki.com/matthewcorven/streaming-digest", result.Metadata.DeepWikiUrl);
    }

    [Fact]
    public void Fetch_returns_null_readme_and_license_content_when_github_documents_are_missing()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.Host == "deepwiki.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><h1>DeepWiki fixture</h1><p>reachable page</p></body></html>", Encoding.UTF8, "text/html")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/readme") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/license") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"description\":\"Streaming Digest\",\"stargazers_count\":42,\"language\":\"C#\",\"default_branch\":\"main\",\"private\":false,\"license\":{\"spdx_id\":\"MIT\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), httpClient);

        var result = service.Fetch("https://github.com/matthewcorven/streaming-digest");

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Repository metadata fetch unexpectedly failed.");
        Assert.NotNull(result.Metadata);
        Assert.Null(result.Metadata!.ReadmeContent);
        Assert.Null(result.Metadata.LicenseContent);
    }

    [Fact]
    public void Fetch_returns_null_readme_and_license_content_for_malformed_or_unsupported_document_payloads()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.Host == "deepwiki.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><h1>DeepWiki fixture</h1><p>reachable page</p></body></html>", Encoding.UTF8, "text/html")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/readme") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"content\":\"not-base64\",\"encoding\":\"base64\"}", Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/license") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"content\":\"TUlU\",\"encoding\":\"utf8\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"description\":\"Streaming Digest\",\"stargazers_count\":42,\"language\":\"C#\",\"default_branch\":\"main\",\"private\":false,\"license\":{\"spdx_id\":\"MIT\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), httpClient);

        var result = service.Fetch("https://github.com/matthewcorven/streaming-digest");

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Repository metadata fetch unexpectedly failed.");
        Assert.NotNull(result.Metadata);
        Assert.Null(result.Metadata!.ReadmeContent);
        Assert.Null(result.Metadata.LicenseContent);
    }

    [Fact]
    public void Fetch_ignores_placeholder_deepwiki_pages()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.Host == "deepwiki.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><h1>Index your code</h1><p>placeholder</p></body></html>", Encoding.UTF8, "text/html")
                };
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/readme") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/license") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"description\":\"Streaming Digest\",\"stargazers_count\":42,\"language\":\"C#\",\"default_branch\":\"main\",\"private\":false,\"license\":{\"spdx_id\":\"MIT\"}}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler);
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), httpClient);

        var result = service.Fetch("https://github.com/matthewcorven/streaming-digest");

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "Repository metadata fetch unexpectedly failed.");
        Assert.NotNull(result.Metadata);
        Assert.Null(result.Metadata!.DeepWikiUrl);
    }

    [Fact]
    public void Fetch_returns_failure_for_unsupported_repository_urls()
    {
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), new HttpClient());

        var result = service.Fetch("https://example.com/docs");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Equal("Unsupported repository URL.", result.ErrorMessage);
    }

    [Fact]
    public void Fetch_returns_rate_limit_error_when_github_replies_with_429()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var httpClient = new HttpClient(handler);
        var service = new RepositoryMetadataService(new RepositoryHostDetectionService(), httpClient);

        var result = service.Fetch("https://github.com/matthewcorven/streaming-digest");

        Assert.False(result.IsSuccess, result.ErrorMessage ?? "Repository metadata fetch unexpectedly succeeded.");
        Assert.True(result.IsRateLimited);
        Assert.Equal(429, result.StatusCode);
        Assert.Equal("GitHub API rate limit exceeded.", result.ErrorMessage);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Send(request, cancellationToken));
        }
    }
}
