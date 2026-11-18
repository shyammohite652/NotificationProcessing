using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.UnitTests.Fakes;

/// <summary>
/// Thread-safe in-memory stand-in for the SQL repository.
/// - Enforces the same UNIQUE DeduplicationKey rule as the database index.
/// - Implements the claim as an atomic compare-and-set (Pending → Processing), like the conditional UPDATE.
/// - Uses the real eligibility/ordering expressions from NotificationQueryExtensions.
/// Task.Yield() is used so concurrent callers actually interleave in the concurrency test.
/// </summary>
public sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly object _gate = new();
    private readonly List<Notification> _items = [];

    public IReadOnlyList<Notification> All
    {
        get { lock (_gate) { return _items.ToList(); } }
    }

    public void Seed(params Notification[] notifications)
    {
        lock (_gate) { _items.AddRange(notifications); }
    }

    public async Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            if (_items.Any(n => n.DeduplicationKey == notification.DeduplicationKey))
            {
                return false;
            }

            _items.Add(notification);
            return true;
        }
    }

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_items.FirstOrDefault(n => n.Id == id)); }
    }

    public Task<Notification?> FindRecentByFingerprintAsync(string fingerprint, DateTimeOffset createdSince, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_items
                .Where(n => n.Fingerprint == fingerprint && n.CreatedAt >= createdSince)
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefault());
        }
    }

    public Task<Notification?> GetByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken)
    {
        lock (_gate) { return Task.FromResult(_items.FirstOrDefault(n => n.DeduplicationKey == deduplicationKey)); }
    }

    public Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetByCustomerAsync(
        string customerId, NotificationStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _items.Where(n => n.CustomerId == customerId && (status == null || n.Status == status)).ToList();
            IReadOnlyList<Notification> pageItems = query
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult((pageItems, query.Count));
        }
    }

    public async Task<IReadOnlyList<Notification>> GetDueForProcessingAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            return _items.AsQueryable()
                .WhereDueForProcessing(now)
                .OrderByProcessingPriority()
                .Take(maxCount)
                .ToList();
        }
    }

    public async Task<bool> TryMarkProcessingAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            var n = _items.FirstOrDefault(x => x.Id == id);
            if (n is null || n.Status != NotificationStatus.Pending)
            {
                return false;
            }

            n.MarkProcessing(now);
            return true;
        }
    }

    public Task<bool> MarkSentAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var n = _items.FirstOrDefault(x => x.Id == id);
            if (n is null || n.Status != NotificationStatus.Processing)
            {
                return Task.FromResult(false);
            }

            n.MarkSent(now);
            return Task.FromResult(true);
        }
    }

    public Task<bool> MarkFailedAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var n = _items.FirstOrDefault(x => x.Id == id);
            if (n is null || n.Status != NotificationStatus.Processing)
            {
                return Task.FromResult(false);
            }

            n.MarkFailed(reason, now);
            return Task.FromResult(true);
        }
    }
}
