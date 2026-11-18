using NotificationApi.Data;
using NotificationApi.Models;
using NotificationApi.Providers;

namespace NotificationApi.Services;

public class NotificationProcessor(
    INotificationRepository repository,
    INotificationProvider provider,
    TimeProvider time,
    ILogger<NotificationProcessor> logger)
{
    public const int BatchSize = 50;

    public async Task<ProcessResult> ProcessPendingAsync(CancellationToken ct)
    {
        // 1. Pending and due, High first, FIFO within a priority. Future notifications are not returned.
        var due = await repository.GetDueAsync(time.GetUtcNow(), BatchSize, ct);
        int sent = 0, failed = 0, skipped = 0;

        foreach (var notification in due)
        {
            // 2. Claim it. If another processing run got there first, skip it.
            if (!await repository.TryClaimAsync(notification.Id, ct))
            {
                skipped++;
                continue;
            }

            // 3. Send. No database transaction is open during this external call.
            string? error = null;
            try
            {
                await provider.SendAsync(notification, ct);
                sent++;
            }
            catch (Exception ex)
            {
                // One failure must not stop the batch or crash the app.
                logger.LogError(ex, "Failed to send notification {NotificationId}", notification.Id);
                error = ex.Message.Length > Notification.FailureReasonMaxLength
                    ? ex.Message[..Notification.FailureReasonMaxLength]
                    : ex.Message;
                failed++;
            }

            // 4. Save the result. CancellationToken.None: the outcome must be recorded even if the caller disconnected.
            await repository.CompleteAsync(
                notification.Id,
                error is null ? NotificationStatus.Sent : NotificationStatus.Failed,
                error,
                time.GetUtcNow(),
                CancellationToken.None);
        }

        logger.LogInformation("Processed {Selected}: {Sent} sent, {Failed} failed, {Skipped} skipped",
            due.Count, sent, failed, skipped);
        return new ProcessResult(due.Count, sent, failed, skipped);
    }
}
