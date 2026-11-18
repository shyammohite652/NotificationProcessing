using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.UnitTests.Fakes;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    public static CreateNotificationRequest ValidRequest(DateTimeOffset now) => new()
    {
        CustomerId = "CUST-101",
        Type = "Email",
        Recipient = "customer@example.com",
        Subject = "Order Confirmed",
        Message = "Your order has been confirmed.",
        Priority = "Normal",
        RequestedDeliveryTime = now.AddHours(1)
    };

    /// <summary>Builds a stored notification directly, for processing tests.</summary>
    public static Notification StoredNotification(
        NotificationPriority priority,
        DateTimeOffset createdAt,
        DateTimeOffset? deliverAt = null,
        string recipient = "customer@example.com",
        string message = "Hello")
    {
        var uniqueMessage = $"{message} {Guid.NewGuid():N}";
        var fingerprint = NotificationFingerprint.Compute(
            "CUST-101", NotificationType.Email, recipient, null, uniqueMessage, priority, deliverAt);

        return Notification.Create(
            "CUST-101",
            NotificationType.Email,
            recipient,
            subject: null,
            uniqueMessage,
            priority,
            deliverAt ?? createdAt,
            createdAt,
            fingerprint,
            NotificationFingerprint.DeduplicationKey(fingerprint, createdAt, TimeSpan.FromMinutes(10)));
    }
}
