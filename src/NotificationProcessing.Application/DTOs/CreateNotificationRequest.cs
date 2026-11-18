namespace NotificationProcessing.Application.DTOs;

/// <summary>
/// Incoming request. Properties are nullable strings on purpose: all validation happens in one place
/// (CreateNotificationRequestValidator) and produces field-level messages, instead of some rules failing
/// early during model binding with generic messages.
/// </summary>
public sealed record CreateNotificationRequest
{
    /// <example>CUST-101</example>
    public string? CustomerId { get; init; }

    /// <summary>Notification channel. Only "Email" is supported.</summary>
    /// <example>Email</example>
    public string? Type { get; init; }

    /// <example>customer@example.com</example>
    public string? Recipient { get; init; }

    /// <example>Order Confirmed</example>
    public string? Subject { get; init; }

    /// <example>Your order has been confirmed.</example>
    public string? Message { get; init; }

    /// <summary>Low, Normal or High (case-insensitive). Defaults to Normal when omitted.</summary>
    /// <example>Normal</example>
    public string? Priority { get; init; }

    /// <summary>When to deliver (ISO 8601 with offset). Omit to deliver as soon as possible. Cannot be in the past.</summary>
    /// <example>2026-12-06T12:30:00Z</example>
    public DateTimeOffset? RequestedDeliveryTime { get; init; }
}
