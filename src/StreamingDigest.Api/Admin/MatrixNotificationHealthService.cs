using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using StreamingDigest.Domain;
using StreamingDigest.Domain.Health;
using StreamingDigest.Infrastructure.Extensions;
using StreamingDigest.MatrixNotifier;
using StreamingDigest.Web.Models;

namespace StreamingDigest.Api.Admin;

public sealed class MatrixNotificationHealthService
{
    private readonly IConfiguration _configuration;
    private readonly IMatrixNotificationService _matrixNotificationService;
    private readonly MatrixNotificationHealthSectionBuilder _matrixSectionBuilder;
    private readonly ILogger<MatrixNotificationHealthService> _logger;

    public MatrixNotificationHealthService(
        IConfiguration configuration,
        IMatrixNotificationService matrixNotificationService,
        ILogger<MatrixNotificationHealthService> logger,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _matrixNotificationService = matrixNotificationService ?? throw new ArgumentNullException(nameof(matrixNotificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _matrixSectionBuilder = new MatrixNotificationHealthSectionBuilder(
            loggerFactory.CreateLogger("StreamingDigest.Api.Admin.MatrixNotificationHealthSectionBuilder"));
    }

    public async Task<MatrixSection> GetSectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Building matrix section from runtime config and recent notification history.");

        try
        {
            var configurationHealth = _matrixNotificationService.GetHealth();
            _logger.LogDebug(
                "Matrix runtime config health loaded: enabled={Enabled}, status={Status}.",
                configurationHealth.Enabled,
                configurationHealth.Status);

            var recentNotifications = await LoadRecentMatrixNotificationsAsync(cancellationToken);
            _logger.LogDebug(
                "Loaded {NotificationCount} Matrix notifications for the recent health window.",
                recentNotifications.Count);

            return _matrixSectionBuilder.Build(configurationHealth, recentNotifications);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error building matrix section: {ErrorMessage}", ex.Message);
            return new MatrixSection
            {
                State = StreamingDigest.Web.Models.HealthState.Error,
                Summary = "Error retrieving Matrix notification status",
                Status = "error",
                Enabled = _matrixNotificationService.IsEnabled,
                Details = [ex.Message],
                PreviewMode = false
            };
        }
    }

    private async Task<List<Notification>> LoadRecentMatrixNotificationsAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetStreamingDigestConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogDebug("Skipping Matrix notification history query because the database connection string is unavailable.");
            return [];
        }

        _logger.LogDebug("Loading Matrix notification history from PostgreSQL.");

        var recentWindowStart = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                status,
                error_summary,
                sent_at,
                created_at,
                updated_at
            FROM public.notifications
            WHERE provider = 'matrix' AND updated_at >= @recent_window_start
            ORDER BY updated_at DESC;
            """,
            connection);
        command.Parameters.AddWithValue("recent_window_start", recentWindowStart);

        var notifications = new List<Notification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            notifications.Add(new Notification
            {
                Provider = "matrix",
                Status = reader.GetString(0),
                ErrorSummary = reader.IsDBNull(1) ? null : reader.GetString(1),
                SentAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4)
            });
        }

        _logger.LogDebug(
            "Matrix notification history query loaded {NotificationCount} rows since {WindowStart}.",
            notifications.Count,
            recentWindowStart);
        return notifications;
    }
}