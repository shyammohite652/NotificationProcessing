using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Services;

/// <summary>
/// Processes due notifications in priority order.
///
/// Per notification:
///   1. Claim it with an atomic conditional UPDATE (Pending → Processing). If 0 rows change, another run owns it: skip.
///   2. Call the provider with NO database transaction open (the external call can be slow).
///   3. Record the outcome (Processing → Sent / Failed).
/// A failure of one notification never stops the rest of the batch.
/// </summary>
public sealed class NotificationProcessor(
    INotificationRepository repository,
    IEnumerable<INotificationProvider> providers,
    TimeProvider timeProvider,
    IOptions<NotificationOptions> options,
    ILogger<NotificationProcessor> logger)
{
    private readonly IReadOnlyDictionary<NotificationType, INotificationProvider> _providers =
        providers.ToDictionary(p => p.Channel);

    private readonly int _batchSize = options.Value.ProcessingBatchSize;

    public async Task<ProcessNotificationsResult> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await repository.GetDueForProcessingAsync(now, _batchSize, cancellationToken);

        int sent = 0, failed = 0, skipped = 0;

        foreach (var notification in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Stop picking up new work; anything not yet claimed stays Pending for the next run.
                break;
            }

            if (!await repository.TryMarkProcessingAsync(notification.Id, timeProvider.GetUtcNow(), cancellationToken))
            {
                skipped++;
                logger.LogDebug("Notification {NotificationId} was already claimed by another processor", notification.Id);
                continue;
            }

            var outcome = await SendSafelyAsync(notification, cancellationToken);

            // The outcome must be recorded even if the caller has gone away, otherwise the row stays in Processing.
            if (outcome.Succeeded)
            {
                await repository.MarkSentAsync(notification.Id, timeProvider.GetUtcNow(), CancellationToken.None);
                sent++;
            }
            else
            {
                var reason = Notification.TruncateFailureReason(outcome.Error);
                await repository.MarkFailedAsync(notification.Id, reason, timeProvider.GetUtcNow(), CancellationToken.None);
                failed++;
            }
        }

        logger.LogInformation(
            "Processing run finished: {Selected} selected, {Sent} sent, {Failed} failed, {Skipped} skipped",
            due.Count, sent, failed, skipped);

        return new ProcessNotificationsResult(due.Count, sent, failed, skipped);
    }

    private async Task<NotificationDeliveryResult> SendSafelyAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(notification.Type, out var provider))
        {
            logger.LogError("No provider registered for channel {Channel}", notification.Type);
            return NotificationDeliveryResult.Failure($"No provider is configured for {notification.Type} notifications.");
        }

        try
        {
            var result = await provider.SendAsync(notification, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Provider failed to deliver notification {NotificationId}: {Error}", notification.Id, result.Error);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // We cannot know whether the provider delivered it; record that honestly instead of guessing.
            logger.LogWarning("Delivery of notification {NotificationId} was cancelled", notification.Id);
            return NotificationDeliveryResult.Failure("Processing was cancelled before the provider confirmed delivery.");
        }
        catch (Exception ex)
        {
            // Never let one provider exception crash the run or the application.
            logger.LogError(ex, "Provider threw while delivering notification {NotificationId}", notification.Id);
            return NotificationDeliveryResult.Failure($"Provider error: {ex.Message}");
        }
    }
}
