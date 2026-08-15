using StreamingDigest.Web.Models;
using StreamingDigest.Web.Services;
using Xunit;

namespace StreamingDigest.Web.UnitTests;

public sealed class SettingsIssueCatalogTests
{
    [Fact]
    public void BuildIssues_NoWarningsOrErrors_ReturnsEmpty()
    {
        var issues = SettingsIssueCatalog.BuildIssues("success", "Admin data loaded from the live API.", SseConnectionState.Connected, []);

        Assert.Empty(issues);
    }

    [Fact]
    public void BuildIssues_PausedConnection_AddsWarning()
    {
        var issues = SettingsIssueCatalog.BuildIssues("success", "Admin data loaded from the live API.", SseConnectionState.Paused, []);

        var issue = Assert.Single(issues);
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Live model updates", issue.Title);
    }

    [Fact]
    public void BuildIssues_DownloadFailure_AddsErrorIssue()
    {
        var row = new ModelRowViewModel("qwen2.5:7b", "Qwen 2.5 7B", "ollama", "llm", "llm", downloadable: true);
        row.TryBeginDownload();
        row.ApplyDownloadFailed("Model pull failed.");

        var issues = SettingsIssueCatalog.BuildIssues("success", "Admin data loaded from the live API.", SseConnectionState.Connected, [row]);

        var issue = Assert.Single(issues);
        Assert.Equal("error", issue.Severity);
        Assert.Equal("Qwen 2.5 7B", issue.Title);
        Assert.Equal("Model pull failed.", issue.Message);
    }

    [Fact]
    public void BuildIssues_OperationalWarning_AddsWarningIssue()
    {
        var issues = SettingsIssueCatalog.BuildIssues("warning", "The admin API is retrying.", SseConnectionState.Connected, []);

        var issue = Assert.Single(issues);
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Operational status", issue.Title);
    }
}