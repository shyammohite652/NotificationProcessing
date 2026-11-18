namespace NotificationProcessing.Domain.Enums;

/// <summary>
/// Stored as an integer so that "ORDER BY Priority DESC" gives High → Normal → Low.
/// Storing the names as strings would sort alphabetically (High, Low, Normal), which is wrong.
/// </summary>
public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}
