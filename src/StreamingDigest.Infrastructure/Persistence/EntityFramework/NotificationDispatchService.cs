using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StreamingDigest.Application.Observability;
using StreamingDigest.Domain;
using StreamingDigest.MatrixNotifier;

namespace StreamingDigest.Infrastructure.Persistence.EntityFramework;

public sealed class NotificationDispatchService(
    StreamingDigestDbContext context,
    IMatrixNotificationService? matrixNotificationService = null) : INotificationDispatchService
{
    public async Task<Notification> QueueDigestNotificationAsync(Digest digest, Guid? operationId, string? target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);

        return await CorrelationContext.RunWithActivityAsync(
            "notification.queue",
            async activity =>
            {
                activity?.SetTag("notification.type", "ingestion_summary");
                activity?.SetTag("notification.provider", "matrix");
                activity?.SetTag("digest.run_type", digest.RunType);
                activity?.SetTag("digest.ingestion_run_id", digest.IngestionRunId.ToString());

                var renderedBody = BuildRenderedBody(digest);
                var payload = new
                {
                    digest.IngestionRunId,
                    digest.RunType,
                    digest.PayloadJson,
                    renderedBody,
                    notificationType = "ingestion_summary"
                };

                var notification = new Notification
                {
                    OperationId = operationId,
                    IngestionRunId = digest.IngestionRunId,
                    NotificationType = "ingestion_summary",
                    Provider = "matrix",
                    Target = string.IsNullOrWhiteSpace(target) ? "matrix" : target,
                    Status = "pending",
                    PayloadJson = JsonSerializer.Serialize(payload),
                    RenderedBody = renderedBody,
                    MessageSummary = renderedBody.Length > 512 ? renderedBody[..512] : renderedBody,
                    AttemptCount = 0,
                    NextRetryAt = DateTimeOffset.UtcNow,
                    ErrorSummary = null
                };

                context.Notifications.Add(notification);
                await context.SaveChangesAsync(cancellationToken);

                var outbox = new OutboxMessage
                {
                    MessageType = "matrix_notification",
                    AggregateType = nameof(Notification),
                    AggregateId = notification.Id,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        notification.Id,
                        notification.Provider,
                        notification.Target,
                        digestId = digest.Id,
                        ingestionRunId = digest.IngestionRunId,
                        payloadJson = digest.PayloadJson,
                        renderedBody,
                        notificationType = notification.NotificationType
                    }),
                    Status = "pending",
                    AttemptCount = 0,
                    NextAttemptAt = DateTimeOffset.UtcNow,
                    LastErrorSummary = null
                };

                context.OutboxMessages.Add(outbox);
                await context.SaveChangesAsync(cancellationToken);

                await DispatchPendingAsync(cancellationToken);

                return notification;
            },
            new Dictionary<string, object?>
            {
                ["notification.type"] = "ingestion_summary",
                ["notification.provider"] = "matrix",
                ["digest.run_type"] = digest.RunType,
                ["digest.ingestion_run_id"] = digest.IngestionRunId
            });
    }

    public async Task<IReadOnlyCollection<OutboxMessage>> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        return await CorrelationContext.RunWithActivityAsync(
            "notification.dispatch_pending",
            async activity =>
            {
                var now = DateTimeOffset.UtcNow;
                var pendingMessages = await context.OutboxMessages
                    .Where(message => message.Status == "pending")
                    .ToListAsync(cancellationToken);

                var dueMessages = pendingMessages
                    .Where(message => message.NextAttemptAt is null || message.NextAttemptAt <= now || message.AttemptCount > 0)
                    .OrderBy(message => message.CreatedAt)
                    .ToList();

                activity?.SetTag("notification.pending_count", pendingMessages.Count.ToString());
                activity?.SetTag("notification.due_count", dueMessages.Count.ToString());

                foreach (var message in dueMessages)
                {
                    var notification = await context.Notifications.SingleOrDefaultAsync(item => item.Id == message.AggregateId, cancellationToken);
                    if (notification is null)
                    {
                        message.Status = "failed";
                        message.LastErrorSummary = "Notification row not found.";
                        message.UpdatedAt = now;
                        continue;
                    }

                    notification.AttemptCount += 1;
                    notification.UpdatedAt = now;
                    message.AttemptCount += 1;
                    message.UpdatedAt = now;
                    message.Status = "processing";
                    message.LastErrorSummary = null;

                    if (matrixNotificationService is null)
                    {
                        notification.Status = "pending";
                        notification.ErrorSummary = "No notification provider is configured.";
                        notification.NextRetryAt = now.AddMinutes(5);
                        message.Status = "pending";
                        message.NextAttemptAt = now.AddMinutes(5);
                        message.LastErrorSummary = notification.ErrorSummary;
                        continue;
                    }

                    try
                    {
                        // Pass the stored target as a room override; "matrix" is the sentinel
                        // written when no per-call target was specified — treat it as the default.
                        var roomOverride = string.IsNullOrWhiteSpace(notification.Target)
                            || string.Equals(notification.Target, "matrix", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : notification.Target;

                        var sendResult = await matrixNotificationService.SendDigestSummaryAsync(new Digest(notification.IngestionRunId ?? Guid.Empty, "standard")
                        {
                            Id = digestIdFromPayload(message.PayloadJson) ?? Guid.Empty,
                            PayloadJson = payloadJsonFromPayload(message.PayloadJson)
                        }, roomOverride, cancellationToken);

                        if (sendResult.Success)
                        {
                            notification.Status = "sent";
                            notification.ProviderMessageId = sendResult.ProviderMessageId ?? sendResult.ResponseBody;
                            notification.ErrorSummary = null;
                            notification.SentAt = now;
                            notification.NextRetryAt = null;
                            message.Status = "sent";
                            message.SentAt = now;
                            message.NextAttemptAt = null;
                            message.LastErrorSummary = null;
                        }
                        else
                        {
                            notification.Status = "pending";
                            notification.ErrorSummary = sendResult.Message;
                            notification.NextRetryAt = now.AddMinutes(5);
                            message.Status = "pending";
                            message.NextAttemptAt = now.AddMinutes(5);
                            message.LastErrorSummary = sendResult.Message;
                        }
                    }
                    catch (Exception ex)
                    {
                        notification.Status = "pending";
                        notification.ErrorSummary = ex.Message;
                        notification.NextRetryAt = now.AddMinutes(5);
                        message.Status = "pending";
                        message.NextAttemptAt = now.AddMinutes(5);
                        message.LastErrorSummary = ex.Message;
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                return pendingMessages;
            },
            new Dictionary<string, object?>
            {
                ["notification.provider"] = "matrix"
            });
    }

    private static string BuildRenderedBody(Digest digest)
    {
        var payload = DigestPayloadSerializer.Deserialize(digest.PayloadJson);
        var sections = new List<string>();

        if (payload.NewVideos.Count > 0)
        {
            sections.Add($"{payload.NewVideos.Count} new video{Pluralize(payload.NewVideos.Count)}");
        }

        if (payload.NewResources.Count > 0)
        {
            sections.Add($"{payload.NewResources.Count} new resource{Pluralize(payload.NewResources.Count)}");
        }

        if (payload.HighSignalMatches.Count > 0)
        {
            sections.Add($"{payload.HighSignalMatches.Count} high-signal match{Pluralize(payload.HighSignalMatches.Count)}");
        }

        if (payload.FailedItems.Count > 0)
        {
            sections.Add($"{payload.FailedItems.Count} failed item{Pluralize(payload.FailedItems.Count)}");
        }

        if (payload.SkippedItems.Count > 0)
        {
            sections.Add($"{payload.SkippedItems.Count} skipped item{Pluralize(payload.SkippedItems.Count)}");
        }

        if (payload.ActiveDeferments.Count > 0)
        {
            sections.Add($"{payload.ActiveDeferments.Count} active deferral{Pluralize(payload.ActiveDeferments.Count)}");
        }

        if (sections.Count == 0)
        {
            sections.Add("no new items");
        }

        return $"Streaming Digest {digest.RunType} run {digest.IngestionRunId:N}: {string.Join(", ", sections)}";
    }

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";

    private static Guid? digestIdFromPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("digestId", out var digestIdElement) && digestIdElement.ValueKind == JsonValueKind.String)
            {
                return Guid.Parse(digestIdElement.GetString()!);
            }

            if (document.RootElement.TryGetProperty("digest_id", out var legacyDigestIdElement) && legacyDigestIdElement.ValueKind == JsonValueKind.String)
            {
                return Guid.Parse(legacyDigestIdElement.GetString()!);
            }
        }
        catch (JsonException)
        {
            // Ignore malformed payloads.
        }

        return null;
    }

    private static string payloadJsonFromPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("payloadJson", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.String)
            {
                return payloadElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed payloads.
        }

        return string.Empty;
    }
}
