using AK.Order.Application.Consumers;
using AK.Order.Application.Sagas;
using AK.ShoppingCart.Application.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AK.IntegrationTests.Common;

public static class TestHarnessFactory
{
    public static ServiceProvider CreateWithSaga(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configure?.Invoke(services);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
               .InMemoryRepository();
        });

        return services.BuildServiceProvider(true);
    }

    public static ServiceProvider CreateWithConsumers(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        configure?.Invoke(services);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
               .InMemoryRepository();
            cfg.AddConsumer<OrderConfirmedConsumer>();
            cfg.AddConsumer<OrderCancelledConsumer>();
            cfg.AddConsumer<ClearCartOnOrderConfirmedConsumer>();
        });

        return services.BuildServiceProvider(true);
    }
}
