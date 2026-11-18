using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Interfaces;

/// <summary>
/// Persistence boundary. It exists because the application rules (dedupe, claiming, ordering) must be
/// unit-testable without SQL Server, and because the status transitions need set-based atomic updates
/// that do not fit a generic "Update(entity)" repository.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Inserts the notification. Returns false (instead of throwing) when the unique DeduplicationKey index
    /// rejects it, i.e. a concurrent identical request won the race.
    /// </summary>
    Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Most recent notification with the same fingerprint created at or after <paramref name="createdSince"/>.</summary>
    Task<Notification?> FindRecentByFingerprintAsync(string fingerprint, DateTimeOffset createdSince, CancellationToken cancellationToken);

    Task<Notification?> GetByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetByCustomerAsync(
        string customerId, NotificationStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Pending and due notifications in processing order (see NotificationQueryExtensions).</summary>
    Task<IReadOnlyList<Notification>> GetDueForProcessingAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Atomic Pending → Processing transition (conditional UPDATE). Returns false if someone else claimed it first.
    /// This is what stops two concurrent processing runs from sending the same notification.
    /// </summary>
    Task<bool> TryMarkProcessingAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Processing → Sent. Returns false if the notification was not in Processing.</summary>
    Task<bool> MarkSentAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Processing → Failed. Returns false if the notification was not in Processing.</summary>
    Task<bool> MarkFailedAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken);
}
