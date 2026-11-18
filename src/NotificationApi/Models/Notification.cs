namespace NotificationApi.Models;

public class Notification
{
    public const int CustomerIdMaxLength = 100;
    public const int RecipientMaxLength = 254;
    public const int SubjectMaxLength = 200;
    public const int MessageMaxLength = 4000;
    public const int FailureReasonMaxLength = 1000;

    public Guid Id { get; set; }
    public string CustomerId { get; set; } = "";
    public NotificationType Type { get; set; }
    public string Recipient { get; set; } = "";
    public string? Subject { get; set; }
    public string Message { get; set; } = "";
    public NotificationPriority Priority { get; set; }
    public DateTimeOffset RequestedDeliveryTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }

    // Hash of the request content + 10-minute time slot. UNIQUE in the database (duplicate protection).
    public string DeduplicationKey { get; set; } = "";
}
