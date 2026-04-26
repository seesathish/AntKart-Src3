using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a cancellation email when an order is cancelled for any reason.
// OrderCancelledIntegrationEvent is published by the OrderSaga when stock reservation fails
// (StockReservationFailedIntegrationEvent) or when the order is manually cancelled by status update.
// The Reason field explains why — e.g. "Stock unavailable" or "Cancelled by user".
public sealed class OrderCancelledConsumer(IMediator mediator) : IConsumer<OrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelledIntegrationEvent> context)
    {
        var msg = context.Message;
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.OrderCancelled,
            msg.CustomerEmail,
            new OrderCancelledModel(msg.CustomerName, msg.OrderNumber, msg.Reason)),
            context.CancellationToken);
    }
}
