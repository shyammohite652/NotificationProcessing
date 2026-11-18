using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Services;

/// <summary>
/// The eligibility and ordering rules for processing, written once as IQueryable expressions.
/// EF Core translates them to SQL; the unit-test fake applies the very same expressions in memory,
/// so the tests exercise the real rules rather than a copy.
/// </summary>
public static class NotificationQueryExtensions
{
    /// <summary>Eligible = still Pending and the requested delivery time has been reached.</summary>
    public static IQueryable<Notification> WhereDueForProcessing(this IQueryable<Notification> query, DateTimeOffset now) =>
        query.Where(n => n.Status == NotificationStatus.Pending && n.RequestedDeliveryTime <= now);

    /// <summary>
    /// High → Normal → Low, then FIFO by CreatedAt. Id is a final tie-breaker so the order is deterministic
    /// when two notifications share the same timestamp.
    /// </summary>
    public static IOrderedQueryable<Notification> OrderByProcessingPriority(this IQueryable<Notification> query) =>
        query.OrderByDescending(n => n.Priority)
             .ThenBy(n => n.CreatedAt)
             .ThenBy(n => n.Id);
}
