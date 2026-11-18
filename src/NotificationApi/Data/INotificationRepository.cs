using NotificationApi.Models;

namespace NotificationApi.Data;

// Lets the services be unit-tested without SQL Server.
public interface INotificationRepository
{
    /// <summary>Returns false if the UNIQUE DeduplicationKey index rejects the insert (a concurrent duplicate).</summary>
    Task<bool> TryAddAsync(Notification notification, CancellationToken ct);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Notification?> FindByDeduplicationKeyAsync(string[] keys, CancellationToken ct);

    Task<(List<Notification> Items, int TotalCount)> GetByCustomerAsync(string customerId, int page, int pageSize, CancellationToken ct);

    Task<List<Notification>> GetDueAsync(DateTimeOffset now, int take, CancellationToken ct);

    /// <summary>Atomically moves Pending to Processing. False means another run claimed it first.</summary>
    Task<bool> TryClaimAsync(Guid id, CancellationToken ct);

    /// <summary>Moves Processing to Sent or Failed.</summary>
    Task CompleteAsync(Guid id, NotificationStatus status, string? failureReason, DateTimeOffset processedAt, CancellationToken ct);
}

public static class NotificationQueries
{
    // Eligible = Pending and due. Order = High first, then oldest first (FIFO); Id only breaks exact ties.
    // Shared by the EF repository and the test fake so the tests check the real rule.
    public static IQueryable<Notification> DueInProcessingOrder(this IQueryable<Notification> query, DateTimeOffset now) =>
        query.Where(n => n.Status == NotificationStatus.Pending && n.RequestedDeliveryTime <= now)
             .OrderByDescending(n => n.Priority)
             .ThenBy(n => n.CreatedAt)
             .ThenBy(n => n.Id);
}
