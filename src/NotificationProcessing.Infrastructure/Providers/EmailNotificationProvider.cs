using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Infrastructure.Providers;

/// <summary>
/// Simulated email gateway. It does not send anything: it waits a little, decides success/failure according to
/// configuration, and logs the outcome. Replace with an SMTP / SendGrid / SES client in a real system.
/// </summary>
internal sealed class EmailNotificationProvider(
    IOptionsMonitor<EmailProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailNotificationProvider> logger) : INotificationProvider
{
    public NotificationType Channel => NotificationType.Email;

    public async Task<NotificationDeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (settings.SimulatedLatency > TimeSpan.Zero)
        {
            await Task.Delay(settings.SimulatedLatency, timeProvider, cancellationToken);
        }

        var domain = notification.Recipient[(notification.Recipient.LastIndexOf('@') + 1)..];

        if (settings.AlwaysFailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogWarning("Simulated email provider rejected notification {NotificationId} (domain {Domain})",
                notification.Id, domain);
            return NotificationDeliveryResult.Failure($"Simulated provider rejected recipient domain '{domain}'.");
        }

        if (settings.FailureRate > 0 && Random.Shared.NextDouble() < settings.FailureRate)
        {
            logger.LogWarning("Simulated email provider timed out for notification {NotificationId}", notification.Id);
            return NotificationDeliveryResult.Failure("Simulated provider timeout.");
        }

        // Log the domain only, not the full address, to keep personal data out of logs.
        logger.LogInformation(
            "Simulated email sent for notification {NotificationId} to a recipient at {Domain} with subject {Subject}",
            notification.Id, domain, notification.Subject);
        return NotificationDeliveryResult.Success();
    }
}
