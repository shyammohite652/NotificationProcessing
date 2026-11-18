using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.DTOs;

public sealed record NotificationResponse(
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
    int AttemptCount,
    DateTimeOffset? ProcessedAt,
    string? FailureReason)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id,
        n.CustomerId,
        n.Type,
        n.Recipient,
        n.Subject,
        n.Message,
        n.Priority,
        n.RequestedDeliveryTime,
        n.CreatedAt,
        n.Status,
        n.AttemptCount,
        n.ProcessedAt,
        n.FailureReason);
}
