using StreamingDigest.Application;

namespace StreamingDigest.UnitTests;

public sealed class RepositoryHostDetectionServiceTests
{
    private readonly IRepositoryHostDetectionService _service = new RepositoryHostDetectionService();

    [Fact]
    public void Detect_returns_github_repository_details_for_repo_urls()
    {
        var result = _service.Detect("https://github.com/matthewcorven/streaming-digest");

        Assert.NotNull(result);
        Assert.Equal(RepositoryHostKind.GitHub, result!.Kind);
        Assert.Equal("github.com", result.Host);
        Assert.Equal("https://github.com/matthewcorven/streaming-digest", result.CanonicalUrl);
        Assert.Equal("matthewcorven", result.Owner);
        Assert.Equal("streaming-digest", result.RepositoryName);
        Assert.True(result.IsRepositoryUrl);
    }

    [Fact]
    public void Detect_canonicalizes_gitlab_repo_urls_with_nested_groups()
    {
        var result = _service.Detect("https://www.gitlab.com/group/subgroup/project/-/issues/1");

        Assert.NotNull(result);
        Assert.Equal(RepositoryHostKind.GitLab, result!.Kind);
        Assert.Equal("https://gitlab.com/group/subgroup/project", result.CanonicalUrl);
        Assert.Equal("group/subgroup", result.Owner);
        Assert.Equal("project", result.RepositoryName);
        Assert.True(result.IsRepositoryUrl);
    }

    [Fact]
    public void Detect_returns_null_for_non_repository_urls()
    {
        var result = _service.Detect("https://example.com/docs/guide");

        Assert.Null(result);
    }
}
