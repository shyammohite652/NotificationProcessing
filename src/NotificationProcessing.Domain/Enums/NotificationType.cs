namespace NotificationProcessing.Domain.Enums;

/// <summary>
/// Delivery channel. Only Email is supported for now; values are explicit because they are persisted as integers.
/// </summary>
public enum NotificationType
{
    Email = 1
}
