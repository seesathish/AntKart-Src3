using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a payment failure alert so the customer knows to retry.
// PaymentFailedIntegrationEvent is published by VerifyPaymentCommandHandler when the
// Razorpay HMAC-SHA256 signature check fails, indicating the payment was not captured.
// The Reason field contains a human-readable explanation (e.g. "Signature mismatch").
public sealed class PaymentFailedConsumer(IMediator mediator) : IConsumer<PaymentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var msg = context.Message;
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.PaymentFailed,
            msg.CustomerEmail,
            new PaymentFailedModel(msg.CustomerName, msg.OrderNumber, msg.Reason)),
            context.CancellationToken);
    }
}
