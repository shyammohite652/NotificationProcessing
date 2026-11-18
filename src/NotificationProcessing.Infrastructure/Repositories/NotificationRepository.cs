using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;
using NotificationProcessing.Infrastructure.Persistence;

namespace NotificationProcessing.Infrastructure.Repositories;

internal sealed class NotificationRepository(NotificationDbContext db) : INotificationRepository
{
    // SQL Server error numbers for unique index (2601) and unique constraint / PK (2627) violations.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            // Detach so this scoped DbContext does not try to insert the entity again.
            db.Entry(notification).State = EntityState.Detached;
            return false;
        }
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<Notification?> FindRecentByFingerprintAsync(string fingerprint, DateTimeOffset createdSince, CancellationToken cancellationToken) =>
        db.Notifications.AsNoTracking()
            .Where(n => n.Fingerprint == fingerprint && n.CreatedAt >= createdSince)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Notification?> GetByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken) =>
        db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.DeduplicationKey == deduplicationKey, cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetByCustomerAsync(
        string customerId, NotificationStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.CustomerId == customerId);
        if (status is { } s)
        {
            query = query.Where(n => n.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Notification>> GetDueForProcessingAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken) =>
        await db.Notifications.AsNoTracking()
            .WhereDueForProcessing(now)
            .OrderByProcessingPriority()
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    // The three transitions below are single UPDATE ... WHERE Id = @id AND Status = @expected statements.
    // SQL Server takes an update lock on the row, so if two runs race, the second one re-checks the WHERE
    // after the first commits, sees Status <> Pending and updates 0 rows. No explicit transaction is needed
    // and nothing is held open while the provider is called.

    public async Task<bool> TryMarkProcessingAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Processing)
                .SetProperty(n => n.ProcessingStartedAt, (DateTimeOffset?)now)
                .SetProperty(n => n.AttemptCount, n => n.AttemptCount + 1),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> MarkSentAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.Status == NotificationStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Sent)
                .SetProperty(n => n.ProcessedAt, (DateTimeOffset?)now)
                .SetProperty(n => n.FailureReason, (string?)null),
                cancellationToken);
        return rows == 1;
    }

    public async Task<bool> MarkFailedAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failureReason = Notification.TruncateFailureReason(reason);
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.Status == NotificationStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Failed)
                .SetProperty(n => n.ProcessedAt, (DateTimeOffset?)now)
                .SetProperty(n => n.FailureReason, failureReason),
                cancellationToken);
        return rows == 1;
    }
}
