using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.ShoppingCart.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AK.ShoppingCart.Application.Consumers;

public sealed class ClearCartOnOrderConfirmedConsumer(
    IUnitOfWork uow,
    ILogger<ClearCartOnOrderConfirmedConsumer> logger) : IConsumer<OrderConfirmedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderConfirmedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;
        logger.LogInformation("Clearing cart for UserId={UserId} after OrderId={OrderId} confirmed",
            userId, context.Message.OrderId);

        var exists = await uow.Carts.ExistsAsync(userId, context.CancellationToken);
        if (!exists)
        {
            logger.LogInformation("No active cart found for UserId={UserId}, skipping clear", userId);
            return;
        }

        await uow.Carts.DeleteAsync(userId, context.CancellationToken);
        logger.LogInformation("Cart cleared for UserId={UserId}", userId);
    }
}
