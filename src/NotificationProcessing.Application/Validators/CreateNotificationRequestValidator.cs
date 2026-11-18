using System.Net.Mail;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Validators;

/// <summary>
/// Single place for all create-notification rules. Hand-written instead of FluentValidation because the
/// rule set is small and one rule depends on the injected clock; an extra package would add little.
/// </summary>
public sealed class CreateNotificationRequestValidator
{
    public IReadOnlyDictionary<string, string[]> Validate(CreateNotificationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        // Customer ID
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            Add("customerId", "Customer ID is required.");
        }
        else if (request.CustomerId.Trim().Length > Notification.CustomerIdMaxLength)
        {
            Add("customerId", $"Customer ID must be at most {Notification.CustomerIdMaxLength} characters.");
        }

        // Type
        NotificationType? type = null;
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            Add("type", "Notification type is required.");
        }
        else if (EnumParser.TryParseName<NotificationType>(request.Type, out var parsedType))
        {
            type = parsedType;
        }
        else
        {
            Add("type", $"Notification type '{request.Type}' is not supported. Supported types: {string.Join(", ", Enum.GetNames<NotificationType>())}.");
        }

        // Recipient (format depends on the type)
        if (string.IsNullOrWhiteSpace(request.Recipient))
        {
            Add("recipient", "Recipient is required.");
        }
        else if (request.Recipient.Trim().Length > Notification.RecipientMaxLength)
        {
            Add("recipient", $"Recipient must be at most {Notification.RecipientMaxLength} characters.");
        }
        else if (type == NotificationType.Email && !IsValidEmail(request.Recipient))
        {
            Add("recipient", "Recipient must be a valid email address.");
        }

        // Subject (optional)
        if (request.Subject is { Length: > Notification.SubjectMaxLength })
        {
            Add("subject", $"Subject must be at most {Notification.SubjectMaxLength} characters.");
        }

        // Message
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            Add("message", "Message is required.");
        }
        else if (request.Message.Length > Notification.MessageMaxLength)
        {
            Add("message", $"Message must be at most {Notification.MessageMaxLength} characters.");
        }

        // Priority (optional, defaults to Normal)
        if (request.Priority is not null && !EnumParser.TryParseName<NotificationPriority>(request.Priority, out _))
        {
            Add("priority", "Priority must be one of: Low, Normal, High.");
        }

        // Requested delivery time: equal to "now" is allowed (it is not in the past); earlier is rejected.
        if (request.RequestedDeliveryTime is { } deliveryTime && deliveryTime < now)
        {
            Add("requestedDeliveryTime", "Requested delivery time cannot be in the past.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    /// <summary>
    /// Uses System.Net.Mail.MailAddress for RFC parsing, but rejects display-name forms such as
    /// "John &lt;john@x.com&gt;" and requires a dot in the domain (rejects "user@localhost").
    /// </summary>
    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!MailAddress.TryCreate(trimmed, out var address) ||
            !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = address.Host;
        return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.');
    }
}
