namespace StreamingDigest.Application.Admin;

/// <summary>
/// Sends a real test notification through the configured notification channel.
/// Abstracted so the Application layer has no dependency on the MatrixNotifier project.
/// The API registers <c>MatrixNotificationTestBridge</c> as the production implementation.
/// </summary>
public interface INotificationTestSender
{
    /// <summary>Returns <c>true</c> when the notification channel is enabled and configured.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Sends the test notification and returns the outcome.
    /// </summary>
    Task<NotificationTestOutcome> TestAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a test notification send attempt.</summary>
public sealed record NotificationTestOutcome(bool Success, string Message);
