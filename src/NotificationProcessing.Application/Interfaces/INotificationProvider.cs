using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Interfaces;

/// <summary>
/// The external delivery service (email gateway, SMS gateway, ...). One implementation per channel.
/// This is the abstraction the exercise asks for: it isolates the unreliable external dependency
/// and lets tests simulate success and failure.
/// </summary>
public interface INotificationProvider
{
    NotificationType Channel { get; }

    /// <summary>
    /// Expected failures (rejected recipient, gateway unavailable) are returned as a failed result.
    /// Unexpected exceptions are also handled by the caller, so a buggy provider cannot stop a batch.
    /// </summary>
    Task<NotificationDeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record NotificationDeliveryResult(bool Succeeded, string? Error)
{
    public static NotificationDeliveryResult Success() => new(true, null);
    public static NotificationDeliveryResult Failure(string error) => new(false, error);
}
