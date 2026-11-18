namespace NotificationProcessing.Domain.Enums;

/// <summary>
/// Lifecycle: Pending → Processing → Sent | Failed.
/// Values are explicit because they are persisted and referenced by a filtered index ([Status] = 0).
/// </summary>
public enum NotificationStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3
}
