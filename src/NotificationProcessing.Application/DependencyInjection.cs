using Microsoft.Extensions.DependencyInjection;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Application.Validators;

namespace NotificationProcessing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<CreateNotificationRequestValidator>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationProcessor>();
        return services;
    }
}
