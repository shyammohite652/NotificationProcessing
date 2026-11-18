using NotificationApi.Models;

namespace NotificationApi.Providers;

// Simulated email gateway: nothing is really sent, the result is only logged.
// Fails for recipients at fail.example.com (for demos) and randomly at EmailProvider:FailureRate (0 to 1).
public class EmailNotificationProvider(IConfiguration config, ILogger<EmailNotificationProvider> logger) : INotificationProvider
{
    public async Task SendAsync(Notification notification, CancellationToken ct)
    {
        await Task.Delay(50, ct); // pretend network call

        var failureRate = config.GetValue("EmailProvider:FailureRate", 0.0);
        if (notification.Recipient.EndsWith("@fail.example.com", StringComparison.OrdinalIgnoreCase)
            || Random.Shared.NextDouble() < failureRate)
        {
            throw new InvalidOperationException("Simulated email provider failure.");
        }

        logger.LogInformation("Email {NotificationId} sent to {Recipient} (priority {Priority})",
            notification.Id, notification.Recipient, notification.Priority);
    }
}
