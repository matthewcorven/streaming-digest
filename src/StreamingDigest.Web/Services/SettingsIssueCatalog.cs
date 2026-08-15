using StreamingDigest.Web.Models;

namespace StreamingDigest.Web.Services;

public static class SettingsIssueCatalog
{
    public static IReadOnlyList<SettingsIssue> BuildIssues(
        string? statusTone,
        string? statusMessage,
        SseConnectionState connectionState,
        IReadOnlyList<ModelRowViewModel> models)
    {
        var issues = new List<SettingsIssue>();

        if (!string.IsNullOrWhiteSpace(statusMessage) && string.Equals(statusTone, "error", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new SettingsIssue("error", "Operational status", statusMessage.Trim(), "operational-status"));
        }
        else if (!string.IsNullOrWhiteSpace(statusMessage) && string.Equals(statusTone, "warning", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new SettingsIssue("warning", "Operational status", statusMessage.Trim(), "operational-status"));
        }

        switch (connectionState)
        {
            case SseConnectionState.Reconnecting:
                issues.Add(new SettingsIssue("warning", "Live model updates", "Reconnecting to live updates. Model statuses will continue to refresh via fallback polling while the stream reconnects.", "model-stream-reconnecting"));
                break;
            case SseConnectionState.Paused:
                issues.Add(new SettingsIssue("warning", "Live model updates", "Live updates paused after repeated stream failures. Use Refresh all to reconcile until the stream recovers.", "model-stream-paused"));
                break;
        }

        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.ErrorMessage))
            {
                continue;
            }

            var severity = model.RowState == ModelRowState.DownloadFailed ? "error" : "warning";
            issues.Add(new SettingsIssue(severity, model.Label, model.ErrorMessage.Trim(), $"model:{model.Id}"));
        }

        return issues;
    }
}

public sealed record SettingsIssue(string Severity, string Title, string Message, string SourceKey);