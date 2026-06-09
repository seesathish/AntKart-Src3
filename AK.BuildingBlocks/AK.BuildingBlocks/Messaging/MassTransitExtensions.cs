using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.BuildingBlocks.Messaging;

// Shared MassTransit + RabbitMQ setup. Every service that needs the event bus calls
// AddRabbitMqMassTransit() from its Infrastructure ServiceCollectionExtensions.
public static class MassTransitExtensions
{
    // Registers MassTransit with RabbitMQ as the transport.
    //
    // The `configure` callback is where each service registers its own consumers:
    //   AddRabbitMqMassTransit(config, "order", x => {
    //       x.AddConsumer<ReserveStockConsumer>();
    //       x.AddSagaStateMachine<OrderSaga, OrderSagaState>()...;
    //   });
    //
    // The `servicePrefix` ensures uniquely-named RabbitMQ queues when multiple services
    // consume the same integration event. Without a prefix, both Order and Notification
    // would create a queue called "payment-failed" and compete (only one receives each
    // message). With prefix "order" → "order-payment-failed"; with "notification" →
    // "notification-payment-failed". Both queues bind to the same exchange so each
    // service receives every message independently (fan-out, not competing consumers).
    //
    // Services that only publish events (no consumers of their own) pass an empty callback: _ => { }
    public static IServiceCollection AddRabbitMqMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        Action<IBusRegistrationConfigurator> configure)
    {
        var host = configuration["RabbitMq:Host"] ?? "localhost";
        var vhost = configuration["RabbitMq:VirtualHost"] ?? "/";
        var username = configuration["RabbitMq:Username"] ?? "guest";
        var password = configuration["RabbitMq:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            // Prefix + kebab-case: "notification" + PaymentFailedConsumer → "notification-payment-failed".
            // Each service gets its own queue per consumer, all bound to the same exchange.
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(servicePrefix, false));

            // Let the caller register service-specific consumers and sagas.
            configure(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(host, vhost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                // Global message retry: if a consumer throws, retry 3 times with
                // incremental delays (1s, 3s, 5s) before moving to the error queue.
                // This handles transient DB or network errors without losing messages.
                cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

                // Auto-configure all registered consumers with their queue names.
                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }
}
