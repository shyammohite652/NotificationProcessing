using NotificationProcessing.Domain.Enums;
using NotificationProcessing.Domain.Exceptions;

namespace NotificationProcessing.Domain.Entities;

/// <summary>
/// A customer notification and its processing state.
/// Setters are private so the only ways to change state are the factory and the transition methods below.
/// </summary>
public sealed class Notification
{
    public const int CustomerIdMaxLength = 100;
    public const int RecipientMaxLength = 254;   // RFC 5321 practical limit for an email address
    public const int SubjectMaxLength = 200;
    public const int MessageMaxLength = 4000;
    public const int FailureReasonMaxLength = 1000;
    public const int FingerprintLength = 64;     // SHA-256 as hex
    public const int DeduplicationKeyMaxLength = 100;

    // Required by EF Core for materialisation.
    private Notification()
    {
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = null!;
    public NotificationType Type { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string? Subject { get; private set; }
    public string Message { get; private set; } = null!;
    public NotificationPriority Priority { get; private set; }

    /// <summary>UTC time at or after which the notification may be sent.</summary>
    public DateTimeOffset RequestedDeliveryTime { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public NotificationStatus Status { get; private set; }

    /// <summary>Number of times a processor has claimed this notification.</summary>
    public int AttemptCount { get; private set; }

    public DateTimeOffset? ProcessingStartedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>SHA-256 of the normalised request content; identifies "the same request".</summary>
    public string Fingerprint { get; private set; } = null!;

    /// <summary>Fingerprint + time bucket. Has a UNIQUE index, which is the database-level duplicate guard.</summary>
    public string DeduplicationKey { get; private set; } = null!;

    public static Notification Create(
        string customerId,
        NotificationType type,
        string recipient,
        string? subject,
        string message,
        NotificationPriority priority,
        DateTimeOffset requestedDeliveryTime,
        DateTimeOffset createdAt,
        string fingerprint,
        string deduplicationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);

        return new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Type = type,
            Recipient = recipient,
            Subject = subject,
            Message = message,
            Priority = priority,
            RequestedDeliveryTime = requestedDeliveryTime.ToUniversalTime(),
            CreatedAt = createdAt.ToUniversalTime(),
            Status = NotificationStatus.Pending,
            AttemptCount = 0,
            Fingerprint = fingerprint,
            DeduplicationKey = deduplicationKey
        };
    }

    public bool IsDueAt(DateTimeOffset now) => Status == NotificationStatus.Pending && RequestedDeliveryTime <= now;

    public void MarkProcessing(DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.Pending, NotificationStatus.Processing);
        Status = NotificationStatus.Processing;
        ProcessingStartedAt = now;
        AttemptCount++;
    }

    public void MarkSent(DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.Processing, NotificationStatus.Sent);
        Status = NotificationStatus.Sent;
        ProcessedAt = now;
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        EnsureStatus(NotificationStatus.Processing, NotificationStatus.Failed);
        Status = NotificationStatus.Failed;
        ProcessedAt = now;
        FailureReason = TruncateFailureReason(reason);
    }

    public static string TruncateFailureReason(string? reason)
    {
        var value = string.IsNullOrWhiteSpace(reason) ? "Unknown provider failure." : reason.Trim();
        return value.Length <= FailureReasonMaxLength ? value : value[..FailureReasonMaxLength];
    }

    private void EnsureStatus(NotificationStatus expected, NotificationStatus target)
    {
        if (Status != expected)
        {
            throw new InvalidNotificationStateException(Id, Status, target);
        }
    }
}
