using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Sagas;
using AK.ShoppingCart.Application.Consumers;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OrderEntity = AK.Order.Domain.Entities.Order;
using OrderConfirmedConsumer = AK.Order.Application.Consumers.OrderConfirmedConsumer;
using OrderCancelledConsumer = AK.Order.Application.Consumers.OrderCancelledConsumer;
using PaymentSucceededConsumer = AK.Order.Application.Consumers.PaymentSucceededConsumer;
using PaymentFailedConsumer = AK.Order.Application.Consumers.PaymentFailedConsumer;
using PaymentsOrderConfirmedConsumer = AK.Payments.Application.Consumers.OrderConfirmedConsumer;
using NotifOrderCreatedConsumer = AK.Notification.Application.Consumers.OrderCreatedConsumer;
using NotifOrderConfirmedConsumer = AK.Notification.Application.Consumers.OrderConfirmedConsumer;
using NotifOrderCancelledConsumer = AK.Notification.Application.Consumers.OrderCancelledConsumer;
using NotifPaymentSucceededConsumer = AK.Notification.Application.Consumers.PaymentSucceededConsumer;
using NotifPaymentFailedConsumer = AK.Notification.Application.Consumers.PaymentFailedConsumer;

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

    // Harness with Order SAGA + payment consumers.
    // PaymentSucceededConsumer and PaymentFailedConsumer require IUnitOfWork — mock returns null
    // so they handle the null-order case (if (order is null) return) without throwing.
    public static ServiceProvider CreateWithPaymentConsumers(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mockRepo = new Mock<IOrderRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrderEntity?)null);

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Orders).Returns(mockRepo.Object);
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        services.AddScoped<IUnitOfWork>(_ => mockUow.Object);

        configure?.Invoke(services);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
               .InMemoryRepository();
            cfg.AddConsumer<PaymentSucceededConsumer>();
            cfg.AddConsumer<PaymentFailedConsumer>();
            cfg.AddConsumer<PaymentsOrderConfirmedConsumer>();
            cfg.AddConsumer<PaymentInitiatedAuditConsumer>();
        });

        return services.BuildServiceProvider(true);
    }

    // Full harness: SAGA + all order consumers + payment consumers.
    public static ServiceProvider CreateWithAllConsumers(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var mockRepo = new Mock<IOrderRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrderEntity?)null);

        var mockUow = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Orders).Returns(mockRepo.Object);
        mockUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        services.AddScoped<IUnitOfWork>(_ => mockUow.Object);

        configure?.Invoke(services);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
               .InMemoryRepository();
            cfg.AddConsumer<OrderConfirmedConsumer>();
            cfg.AddConsumer<OrderCancelledConsumer>();
            cfg.AddConsumer<PaymentSucceededConsumer>();
            cfg.AddConsumer<PaymentFailedConsumer>();
            cfg.AddConsumer<PaymentsOrderConfirmedConsumer>();
            cfg.AddConsumer<PaymentInitiatedAuditConsumer>();
        });

        return services.BuildServiceProvider(true);
    }

    // Harness with all 5 notification consumers. Pass a pre-configured mediator mock so tests can verify Send calls.
    public static ServiceProvider CreateWithNotificationConsumers(Mock<IMediator> mediatorMock)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddScoped<IMediator>(_ => mediatorMock.Object);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<NotifOrderCreatedConsumer>();
            cfg.AddConsumer<NotifOrderConfirmedConsumer>();
            cfg.AddConsumer<NotifOrderCancelledConsumer>();
            cfg.AddConsumer<NotifPaymentSucceededConsumer>();
            cfg.AddConsumer<NotifPaymentFailedConsumer>();
        });

        return services.BuildServiceProvider(true);
    }
}
