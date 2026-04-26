using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a stock-confirmed email when the Order SAGA moves from Pending → Confirmed.
// Triggered by OrderConfirmedIntegrationEvent published by the OrderSaga after
// StockReservedIntegrationEvent is received from AK.Products.
public sealed class OrderConfirmedConsumer(IMediator mediator) : IConsumer<OrderConfirmedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context)
    {
        var msg = context.Message;
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.OrderConfirmed,
            msg.CustomerEmail,
            new OrderConfirmedModel(msg.CustomerName, msg.OrderNumber, msg.TotalAmount)),
            context.CancellationToken);
    }
}
