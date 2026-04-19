using AK.BuildingBlocks.Messaging.IntegrationEvents;
using MassTransit;

namespace AK.Payments.Application.Consumers;

public sealed class OrderConfirmedConsumer : IConsumer<OrderConfirmedIntegrationEvent>
{
    public Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context) => Task.CompletedTask;
}
