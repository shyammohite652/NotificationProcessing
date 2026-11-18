namespace NotificationApi.Models;

// Strings (not enums) so that every rule, including "priority must be Low/Normal/High",
// is checked in one place (NotificationValidator) and returns a clear field-level message.
public record CreateNotificationRequest
{
    public string? CustomerId { get; init; }
    public string? Type { get; init; }
    public string? Recipient { get; init; }
    public string? Subject { get; init; }
    public string? Message { get; init; }
    public string? Priority { get; init; }                     // optional, default Normal
    public DateTimeOffset? RequestedDeliveryTime { get; init; } // optional, default "now"
}

public record NotificationResponse(
    Guid Id,
    string CustomerId,
    NotificationType Type,
    string Recipient,
    string? Subject,
    string Message,
    NotificationPriority Priority,
    DateTimeOffset RequestedDeliveryTime,
    DateTimeOffset CreatedAt,
    NotificationStatus Status,
    DateTimeOffset? ProcessedAt,
    string? FailureReason)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id, n.CustomerId, n.Type, n.Recipient, n.Subject, n.Message, n.Priority,
        n.RequestedDeliveryTime, n.CreatedAt, n.Status, n.ProcessedAt, n.FailureReason);
}

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record ProcessResult(int Selected, int Sent, int Failed, int Skipped);
