using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Infrastructure.Persistence;
using NotificationProcessing.Infrastructure.Providers;
using NotificationProcessing.Infrastructure.Repositories;

namespace NotificationProcessing.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "NotificationDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. See README → Connection string configuration.");

        services.AddDbContext<NotificationDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddOptions<EmailProviderOptions>()
            .Bind(configuration.GetSection(EmailProviderOptions.SectionName))
            .Validate(o => o.FailureRate is >= 0 and <= 1, "EmailProvider:FailureRate must be between 0 and 1.")
            .ValidateOnStart();

        // Stateless, so one instance is enough. Registered as INotificationProvider so more channels can be added.
        services.AddSingleton<INotificationProvider, EmailNotificationProvider>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations. Used at startup in Development only.</summary>
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
