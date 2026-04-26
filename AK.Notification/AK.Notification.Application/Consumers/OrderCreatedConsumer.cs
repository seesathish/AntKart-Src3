using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends an order confirmation email when a new order is placed.
// Triggered by OrderCreatedIntegrationEvent published by AK.Order's CreateOrderCommandHandler.
// Formats item lines as "2x MEN-SHIR-001 @ ₹499.00" for the email body.
public sealed class OrderCreatedConsumer(IMediator mediator) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var msg = context.Message;

        // Build a human-readable summary of each line item for the email template.
        var itemSummaries = msg.Items
            .Select(i => $"{i.Quantity}x {i.Sku} @ ₹{i.UnitPrice:N2}")
            .ToList();

        // Delegate to SendNotificationCommand — which renders the template, persists the record,
        // and delivers via the Email channel.
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.OrderConfirmation,
            msg.CustomerEmail,
            new OrderConfirmationModel(msg.CustomerName, msg.OrderNumber, msg.TotalAmount, itemSummaries)),
            context.CancellationToken);
    }
}
