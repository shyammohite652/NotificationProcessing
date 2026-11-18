namespace NotificationApi.Models;

public enum NotificationType
{
    Email = 1
}

// Stored as int so ORDER BY Priority DESC gives High, Normal, Low.
// (As text it would sort alphabetically: High, Low, Normal.)
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public enum NotificationStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3
}
