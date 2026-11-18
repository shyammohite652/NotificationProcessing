using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NotificationApi.Models;

namespace NotificationApi.Data;

public class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public async Task<bool> TryAddAsync(Notification notification, CancellationToken ct)
    {
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) // unique violation
        {
            db.Entry(notification).State = EntityState.Detached;
            return false;
        }
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<Notification?> FindByDeduplicationKeyAsync(string[] keys, CancellationToken ct) =>
        db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => keys.Contains(n.DeduplicationKey), ct);

    public async Task<(List<Notification> Items, int TotalCount)> GetByCustomerAsync(
        string customerId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.CustomerId == customerId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(ct);
        return (items, total);
    }

    public Task<List<Notification>> GetDueAsync(DateTimeOffset now, int take, CancellationToken ct) =>
        db.Notifications.AsNoTracking().DueInProcessingOrder(now).Take(take).ToListAsync(ct);

    // UPDATE Notifications SET Status = Processing WHERE Id = @id AND Status = Pending
    // SQL Server locks the row, so if two runs race, the second one updates 0 rows and skips it.
    public async Task<bool> TryClaimAsync(Guid id, CancellationToken ct) =>
        await db.Notifications
            .Where(n => n.Id == id && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Processing), ct) == 1;

    public Task CompleteAsync(Guid id, NotificationStatus status, string? failureReason, DateTimeOffset processedAt, CancellationToken ct) =>
        db.Notifications
            .Where(n => n.Id == id && n.Status == NotificationStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, status)
                .SetProperty(n => n.FailureReason, failureReason)
                .SetProperty(n => n.ProcessedAt, (DateTimeOffset?)processedAt), ct);
}
