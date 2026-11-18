using System.Security.Cryptography;
using System.Text;
using NotificationApi.Data;
using NotificationApi.Models;

namespace NotificationApi.Services;

public class NotificationService(INotificationRepository repository, TimeProvider time, ILogger<NotificationService> logger)
{
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

    public async Task<(NotificationResponse Notification, bool IsDuplicate)> CreateAsync(
        CreateNotificationRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var errors = NotificationValidator.Validate(request, now);
        if (errors.Count > 0)
            throw new ValidationException(errors);

        NotificationValidator.TryParseEnum<NotificationType>(request.Type, out var type);
        var priority = NotificationValidator.TryParseEnum<NotificationPriority>(request.Priority, out var p)
            ? p
            : NotificationPriority.Normal;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId!.Trim(),
            Type = type,
            Recipient = request.Recipient!.Trim(),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
            Message = request.Message!,
            Priority = priority,
            RequestedDeliveryTime = (request.RequestedDeliveryTime ?? now).ToUniversalTime(),
            CreatedAt = now,
            Status = NotificationStatus.Pending
        };

        // Duplicate check. Key = hash of the request + 10-minute slot.
        // Looking in the current AND previous slot means a retry within 10 minutes is always caught.
        var hash = Hash(notification, request.RequestedDeliveryTime);
        var slot = now.ToUnixTimeSeconds() / (long)DuplicateWindow.TotalSeconds;
        notification.DeduplicationKey = $"{hash}:{slot}";

        var existing = await repository.FindByDeduplicationKeyAsync([notification.DeduplicationKey, $"{hash}:{slot - 1}"], ct);
        if (existing is not null)
        {
            logger.LogInformation("Duplicate request; returning existing notification {NotificationId}", existing.Id);
            return (NotificationResponse.From(existing), true);
        }

        if (await repository.TryAddAsync(notification, ct))
        {
            logger.LogInformation("Notification {NotificationId} created with priority {Priority}", notification.Id, priority);
            return (NotificationResponse.From(notification), false);
        }

        // Two identical requests arrived at the same time; the unique index let only one in. Return that one.
        var winner = await repository.FindByDeduplicationKeyAsync([notification.DeduplicationKey], ct)
                     ?? throw new InvalidOperationException("Duplicate insert rejected but original not found.");
        return (NotificationResponse.From(winner), true);
    }

    public async Task<NotificationResponse?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var notification = await repository.GetByIdAsync(id, ct);
        return notification is null ? null : NotificationResponse.From(notification);
    }

    public async Task<PagedResponse<NotificationResponse>> GetForCustomerAsync(
        string customerId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await repository.GetByCustomerAsync(customerId.Trim(), page, pageSize, ct);
        return new PagedResponse<NotificationResponse>(items.Select(NotificationResponse.From).ToList(), page, pageSize, total);
    }

    // The fields that make two requests "the same". Recipient ignores case.
    private static string Hash(Notification n, DateTimeOffset? requestedDeliveryTime)
    {
        var text = string.Join('|',
            n.CustomerId, n.Type, n.Recipient.ToLowerInvariant(), n.Subject, n.Message, n.Priority,
            requestedDeliveryTime?.UtcDateTime.ToString("O") ?? "now");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
