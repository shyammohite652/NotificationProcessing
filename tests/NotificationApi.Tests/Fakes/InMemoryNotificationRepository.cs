using NotificationApi.Data;
using NotificationApi.Models;

namespace NotificationApi.Tests.Fakes;

// Thread-safe stand-in for the SQL repository. It copies the two database behaviours the services rely on:
// the UNIQUE DeduplicationKey and the atomic Pending → Processing claim. It uses the real ordering query.
public class InMemoryNotificationRepository : INotificationRepository
{
    private readonly object _lock = new();
    public List<Notification> Items { get; } = [];

    public async Task<bool> TryAddAsync(Notification notification, CancellationToken ct)
    {
        await Task.Yield();
        lock (_lock)
        {
            if (Items.Any(n => n.DeduplicationKey == notification.DeduplicationKey)) return false;
            Items.Add(notification);
            return true;
        }
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(Items.FirstOrDefault(n => n.Id == id));
    }

    public Task<Notification?> FindByDeduplicationKeyAsync(string[] keys, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(Items.FirstOrDefault(n => keys.Contains(n.DeduplicationKey)));
    }

    public Task<(List<Notification> Items, int TotalCount)> GetByCustomerAsync(string customerId, int page, int pageSize, CancellationToken ct)
    {
        lock (_lock)
        {
            var all = Items.Where(n => n.CustomerId == customerId).OrderByDescending(n => n.CreatedAt).ToList();
            return Task.FromResult((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
        }
    }

    public async Task<List<Notification>> GetDueAsync(DateTimeOffset now, int take, CancellationToken ct)
    {
        await Task.Yield();
        lock (_lock) return Items.AsQueryable().DueInProcessingOrder(now).Take(take).ToList();
    }

    public async Task<bool> TryClaimAsync(Guid id, CancellationToken ct)
    {
        await Task.Yield();
        lock (_lock)
        {
            var n = Items.First(x => x.Id == id);
            if (n.Status != NotificationStatus.Pending) return false;
            n.Status = NotificationStatus.Processing;
            return true;
        }
    }

    public Task CompleteAsync(Guid id, NotificationStatus status, string? failureReason, DateTimeOffset processedAt, CancellationToken ct)
    {
        lock (_lock)
        {
            var n = Items.First(x => x.Id == id);
            if (n.Status == NotificationStatus.Processing)
            {
                n.Status = status;
                n.FailureReason = failureReason;
                n.ProcessedAt = processedAt;
            }
        }
        return Task.CompletedTask;
    }
}
