using StreamingDigest.Application.Admin;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Api.Admin;

/// <summary>
/// Bridges the Application-layer <see cref="INotificationTestSender"/> to the
/// <see cref="IMatrixNotificationService"/> so <see cref="AdminOperationsService"/> can
/// send a real test notification without taking a dependency on the MatrixNotifier project.
/// </summary>
internal sealed class MatrixNotificationTestBridge(IMatrixNotificationService matrixService) : INotificationTestSender
{
    public bool IsEnabled => matrixService.IsEnabled;

    public async Task<NotificationTestOutcome> TestAsync(CancellationToken cancellationToken = default)
    {
        var result = await matrixService.SendTestNotificationAsync(cancellationToken);
        return new NotificationTestOutcome(result.Success, result.Message);
    }
}
