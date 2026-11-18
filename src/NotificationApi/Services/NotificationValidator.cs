using System.Net.Mail;
using NotificationApi.Models;

namespace NotificationApi.Services;

public class ValidationException(Dictionary<string, string[]> errors) : Exception("Validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}

public static class NotificationValidator
{
    public static Dictionary<string, string[]> Validate(CreateNotificationRequest r, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(r.CustomerId))
            errors["customerId"] = ["Customer ID is required."];
        else if (r.CustomerId.Trim().Length > Notification.CustomerIdMaxLength)
            errors["customerId"] = [$"Customer ID must be at most {Notification.CustomerIdMaxLength} characters."];

        if (string.IsNullOrWhiteSpace(r.Type))
            errors["type"] = ["Notification type is required."];
        else if (!TryParseEnum<NotificationType>(r.Type, out _))
            errors["type"] = ["Only 'Email' notifications are supported."];

        if (string.IsNullOrWhiteSpace(r.Recipient))
            errors["recipient"] = ["Recipient is required."];
        else if (!IsValidEmail(r.Recipient))
            errors["recipient"] = ["Recipient must be a valid email address."];

        if (r.Subject?.Length > Notification.SubjectMaxLength)
            errors["subject"] = [$"Subject must be at most {Notification.SubjectMaxLength} characters."];

        if (string.IsNullOrWhiteSpace(r.Message))
            errors["message"] = ["Message is required."];
        else if (r.Message.Length > Notification.MessageMaxLength)
            errors["message"] = [$"Message must be at most {Notification.MessageMaxLength} characters."];

        if (r.Priority is not null && !TryParseEnum<NotificationPriority>(r.Priority, out _))
            errors["priority"] = ["Priority must be Low, Normal or High."];

        // Equal to "now" is allowed; only earlier times are in the past.
        if (r.RequestedDeliveryTime < now)
            errors["requestedDeliveryTime"] = ["Requested delivery time cannot be in the past."];

        return errors;
    }

    // Case-insensitive name match. Numbers like "2" are rejected (Enum.TryParse would accept them).
    public static bool TryParseEnum<T>(string? value, out T result) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out result)
        && Enum.IsDefined(result)
        && !int.TryParse(value, out _);

    // MailAddress alone accepts "John <john@x.com>" and "user@localhost", so check those too.
    public static bool IsValidEmail(string value)
    {
        var email = value.Trim();
        return email.Length <= Notification.RecipientMaxLength
            && MailAddress.TryCreate(email, out var address)
            && address.Address == email
            && address.Host.Contains('.');
    }
}
