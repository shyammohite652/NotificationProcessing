using NotificationApi.Models;

namespace NotificationApi.Providers;

// The external delivery service. Throws if sending fails.
public interface INotificationProvider
{
    Task SendAsync(Notification notification, CancellationToken ct);
}
