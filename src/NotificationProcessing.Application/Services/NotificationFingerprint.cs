using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Services;

/// <summary>
/// Builds the identity of a request for duplicate detection.
///
/// Fields included: customer, type, recipient (case-insensitive), subject, message, priority and requested delivery
/// time. If any of these differ, the client is asking for a different notification. Recipient casing is ignored
/// because mail systems treat it case-insensitively in practice; subject and message are compared exactly.
/// </summary>
public static class NotificationFingerprint
{
    private const char Separator = '\u001F'; // unit separator – cannot appear in normal text, avoids "ab"+"c" == "a"+"bc"

    public static string Compute(
        string customerId,
        NotificationType type,
        string recipient,
        string? subject,
        string message,
        NotificationPriority priority,
        DateTimeOffset? requestedDeliveryTime)
    {
        var canonical = string.Join(Separator,
            customerId,
            type.ToString(),
            recipient.ToLowerInvariant(),
            subject ?? string.Empty,
            message,
            priority.ToString(),
            requestedDeliveryTime?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "asap");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash); // 64 upper-case hex characters
    }

    /// <summary>
    /// Fingerprint + fixed time bucket. A UNIQUE index on this value makes the database reject a second
    /// identical insert even when two requests arrive at the same moment and both pass the application check.
    /// </summary>
    public static string DeduplicationKey(string fingerprint, DateTimeOffset now, TimeSpan window)
    {
        var bucket = now.ToUnixTimeMilliseconds() / (long)window.TotalMilliseconds;
        return string.Create(CultureInfo.InvariantCulture, $"{fingerprint}:{bucket}");
    }
}
