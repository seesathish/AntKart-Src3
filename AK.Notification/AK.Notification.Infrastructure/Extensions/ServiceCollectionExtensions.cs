using AK.BuildingBlocks.Email;
using AK.BuildingBlocks.Messaging;
using MassTransit;
using AK.Notification.Application.Channels;
using AK.Notification.Application.Repositories;
using AK.Notification.Application.Templates;
using AK.Notification.Application.Consumers;
using AK.Notification.Infrastructure.Channels;
using AK.Notification.Infrastructure.Persistence;
using AK.Notification.Infrastructure.Persistence.Repositories;
using AK.Notification.Infrastructure.Services;
using AK.Notification.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Notification.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        services.AddDbContext<NotificationsDbContext>(opts =>
            opts.UseNpgsql(connStr));

        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<INotificationChannel, SmsNotificationChannel>();
        services.AddScoped<INotificationChannel, WhatsAppNotificationChannel>();

        services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();
        services.AddScoped<INotificationTemplateRenderer, NotificationTemplateRenderer>();

        services.AddHostedService<NotificationCleanupService>();

        // Email is sent via Azure Communication Services (Entra/managed-identity by default; the
        // EmailNotificationChannel delegates to this shared IEmailSender).
        services.AddAcsEmailSender(configuration);
        services.Configure<NotificationSettings>(configuration.GetSection("NotificationSettings"));

        services.AddAzureServiceBusMassTransit(
            configuration,
            x =>
            {
                x.AddConsumer<OrderCreatedConsumer>();
                x.AddConsumer<OrderConfirmedConsumer>();
                x.AddConsumer<OrderCancelledConsumer>();
                x.AddConsumer<PaymentSucceededConsumer>();
                x.AddConsumer<PaymentFailedConsumer>();
            },
            // Bind the provisioned "notification" subscription on the integration-events topic.
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("notification", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureConsumer<OrderCreatedConsumer>(context);
                    e.ConfigureConsumer<OrderConfirmedConsumer>(context);
                    e.ConfigureConsumer<OrderCancelledConsumer>(context);
                    e.ConfigureConsumer<PaymentSucceededConsumer>(context);
                    e.ConfigureConsumer<PaymentFailedConsumer>(context);
                });
            });

        return services;
    }
}
