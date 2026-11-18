using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Domain.Exceptions;

/// <summary>
/// Thrown when code tries to move a notification through an illegal status transition
/// (for example Sent → Processing). This indicates a programming error, not a client error.
/// </summary>
public sealed class InvalidNotificationStateException(Guid notificationId, NotificationStatus current, NotificationStatus target)
    : InvalidOperationException($"Notification {notificationId} cannot move from {current} to {target}.")
{
    public Guid NotificationId { get; } = notificationId;
    public NotificationStatus CurrentStatus { get; } = current;
    public NotificationStatus TargetStatus { get; } = target;
}
