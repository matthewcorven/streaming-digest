using Microsoft.Extensions.Logging;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Admin;

public sealed class MatrixNotificationHealthSectionBuilder
{
    private readonly ILogger _logger;

    public MatrixNotificationHealthSectionBuilder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MatrixSection Build(MatrixHealthInfo configurationHealth, IReadOnlyList<Notification> recentNotifications)
    {
        ArgumentNullException.ThrowIfNull(configurationHealth);
        ArgumentNullException.ThrowIfNull(recentNotifications);

        _logger.LogDebug(
            "Building matrix admin health section from configuration status {Status} and {NotificationCount} recent notifications.",
            configurationHealth.Status,
            recentNotifications.Count);

        if (!configurationHealth.Enabled)
        {
            _logger.LogDebug("Matrix admin health resolved as degraded because notifications are disabled.");
            return CreateSection(
                HealthState.Degraded,
                configurationHealth.Status,
                configurationHealth.Enabled,
                "Matrix notifications are disabled.",
                recentNotifications,
                [configurationHealth.Reason]);
        }

        if (!string.Equals(configurationHealth.Status, "ready", StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Matrix admin health resolved as degraded because configuration is incomplete: {Status}.",
                configurationHealth.Status);
            return CreateSection(
                HealthState.Degraded,
                configurationHealth.Status,
                configurationHealth.Enabled,
                configurationHealth.Reason,
                recentNotifications,
                [configurationHealth.Reason]);
        }

        var recentSuccessCount = recentNotifications.Count(IsSuccessfulNotification);
        var recentFailureCount = recentNotifications.Count(IsFailedNotification);
        var latestFailure = recentNotifications
            .Where(IsFailedNotification)
            .OrderByDescending(notification => notification.UpdatedAt)
            .FirstOrDefault();

        if (recentFailureCount > 0 && recentSuccessCount == 0)
        {
            _logger.LogDebug(
                "Matrix admin health resolved as error because {FailureCount} recent delivery attempts failed without any success.",
                recentFailureCount);
            return CreateSection(
                HealthState.Error,
                "delivery-failed",
                configurationHealth.Enabled,
                "Recent Matrix delivery attempts are failing.",
                recentNotifications,
                BuildDeliveryDetails(recentSuccessCount, recentFailureCount, latestFailure));
        }

        if (recentFailureCount > 0)
        {
            _logger.LogDebug(
                "Matrix admin health resolved as degraded because {FailureCount} recent delivery attempts need attention.",
                recentFailureCount);
            return CreateSection(
                HealthState.Degraded,
                "delivery-degraded",
                configurationHealth.Enabled,
                "Recent Matrix delivery attempts need attention.",
                recentNotifications,
                BuildDeliveryDetails(recentSuccessCount, recentFailureCount, latestFailure));
        }

        if (recentSuccessCount > 0)
        {
            _logger.LogDebug(
                "Matrix admin health resolved as ready because {SuccessCount} recent delivery attempts succeeded.",
                recentSuccessCount);
            return CreateSection(
                HealthState.Ready,
                "ready",
                configurationHealth.Enabled,
                "Matrix notifications are operational.",
                recentNotifications,
                BuildDeliveryDetails(recentSuccessCount, recentFailureCount, latestFailure));
        }

        _logger.LogDebug("Matrix admin health resolved as ready because configuration is valid and no recent deliveries require verification.");
        return CreateSection(
            HealthState.Ready,
            "idle",
            configurationHealth.Enabled,
            "Matrix notifications are configured; no recent deliveries to verify.",
            recentNotifications,
            ["No Matrix delivery attempts were recorded in the last 5 minutes."]);
    }

    private static MatrixSection CreateSection(
        HealthState state,
        string status,
        bool enabled,
        string summary,
        IReadOnlyList<Notification> recentNotifications,
        IReadOnlyList<string> details)
    {
        return new MatrixSection
        {
            State = state,
            Status = status,
            Enabled = enabled,
            Summary = summary,
            RecentSuccessCount = recentNotifications.Count(IsSuccessfulNotification),
            RecentFailureCount = recentNotifications.Count(IsFailedNotification),
            LastAttemptAt = recentNotifications.Count == 0
                ? null
                : recentNotifications.Max(notification => (notification.SentAt ?? notification.UpdatedAt)).UtcDateTime,
            LastSuccessAt = recentNotifications
                .Where(IsSuccessfulNotification)
                .Select(notification => notification.SentAt)
                .Where(sentAt => sentAt.HasValue)
                .Select(sentAt => sentAt!.Value.UtcDateTime)
                .DefaultIfEmpty()
                .Max(),
            Details = details,
            PreviewMode = false
        };
    }

    private static IReadOnlyList<string> BuildDeliveryDetails(int recentSuccessCount, int recentFailureCount, Notification? latestFailure)
    {
        var details = new List<string>();
        if (recentSuccessCount > 0)
        {
            details.Add($"{recentSuccessCount} successful Matrix delivery attempts in the last 5 minutes.");
        }

        if (recentFailureCount > 0)
        {
            details.Add($"{recentFailureCount} failed or retrying Matrix delivery attempts in the last 5 minutes.");
        }

        if (!string.IsNullOrWhiteSpace(latestFailure?.ErrorSummary))
        {
            details.Add($"Latest Matrix delivery error: {latestFailure.ErrorSummary}");
        }

        return details;
    }

    private static bool IsSuccessfulNotification(Notification notification)
        => string.Equals(notification.Status, "sent", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedNotification(Notification notification)
        => string.Equals(notification.Status, "failed", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(notification.Status, "pending", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(notification.ErrorSummary));
}