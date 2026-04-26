using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a payment receipt email after Razorpay signature verification succeeds.
// PaymentSucceededIntegrationEvent is published by VerifyPaymentCommandHandler in AK.Payments.
// RazorpayPaymentId (e.g. pay_ABC123) is included so the customer can quote it for support.
public sealed class PaymentSucceededConsumer(IMediator mediator) : IConsumer<PaymentSucceededIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var msg = context.Message;
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.PaymentSucceeded,
            msg.CustomerEmail,
            new PaymentSucceededModel(msg.CustomerName, msg.OrderNumber, msg.Amount, msg.RazorpayPaymentId)),
            context.CancellationToken);
    }
}
