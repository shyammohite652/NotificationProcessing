namespace NotificationProcessing.Application.DTOs;

/// <param name="Notification">The stored notification (the original one when the request was a duplicate).</param>
/// <param name="IsDuplicate">True when an identical request was already accepted within the duplicate window.</param>
public sealed record CreateNotificationResult(NotificationResponse Notification, bool IsDuplicate);
