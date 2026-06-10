using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Resilience;
using AK.Order.Application.Consumers;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Sagas;
using AK.Order.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Order.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        services.AddDbContext<OrderDbContext>(opts =>
            opts.UseNpgsql(connStr));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddNpgsqlResilience();

        services.AddAzureServiceBusMassTransit(
            configuration,
            // Register the saga (with its EF repository), the outbox, and the event consumers.
            x =>
            {
                x.AddSagaStateMachine<OrderSaga, OrderSagaState>()
                 .EntityFrameworkRepository(r =>
                 {
                     r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                     r.ExistingDbContext<OrderDbContext>();
                     r.UsePostgres();
                 });

                x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });

                x.AddConsumer<OrderConfirmedConsumer>();
                x.AddConsumer<OrderCancelledConsumer>();
                x.AddConsumer<PaymentSucceededConsumer>();
                x.AddConsumer<PaymentFailedConsumer>();
            },
            // Bind to the provisioned "order" subscription on the integration-events topic. The
            // saga and the event consumers all consume PUBLISHED integration events, so they read
            // from this subscription. (The provisioned order-commands queue is reserved for
            // command-style messaging; the current order workflow is event-driven.)
            (context, cfg) =>
            {
                cfg.SubscriptionEndpoint("order", MassTransitExtensions.IntegrationEventsTopic, e =>
                {
                    e.ConfigureSaga<OrderSagaState>(context);
                    e.ConfigureConsumer<OrderConfirmedConsumer>(context);
                    e.ConfigureConsumer<OrderCancelledConsumer>(context);
                    e.ConfigureConsumer<PaymentSucceededConsumer>(context);
                    e.ConfigureConsumer<PaymentFailedConsumer>(context);
                });
            });

        return services;
    }
}
