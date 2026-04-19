using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Enums;
using MassTransit;

namespace AK.Order.Application.Consumers;

public sealed class PaymentSucceededConsumer(IUnitOfWork uow) : IConsumer<PaymentSucceededIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentSucceededIntegrationEvent> context)
    {
        var order = await uow.Orders.GetByIdAsync(context.Message.OrderId, context.CancellationToken);
        if (order is null) return;
        order.UpdateStatus(OrderStatus.Paid);
        order.ConfirmPayment();
        await uow.SaveChangesAsync(context.CancellationToken);
    }
}
