using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

// Reacts to a failed Razorpay payment (published by AK.Payments when signature verification fails
// or the user cancels the payment widget).
// Marks the order as PaymentFailed — the customer can retry payment or the order can be cancelled.
public sealed class PaymentFailedConsumer(IUnitOfWork uow) : IConsumer<PaymentFailedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedIntegrationEvent> context)
    {
        var order = await uow.Orders.GetByIdAsync(context.Message.OrderId, context.CancellationToken);
        if (order is null) return;

        // PaymentFailed is a valid transition from Confirmed (see _allowedTransitions in Order entity).
        order.UpdateStatus(OrderStatus.PaymentFailed);
        await uow.SaveChangesAsync(context.CancellationToken);
    }
}
