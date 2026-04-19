using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Products.Application.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AK.Products.Application.Consumers;

public sealed class ReserveStockConsumer(
    IUnitOfWork uow,
    ILogger<ReserveStockConsumer> logger) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Reserving stock for OrderId={OrderId}", msg.OrderId);

        // Check stock sufficiency first (read-then-validate)
        var insufficientItems = new List<string>();
        foreach (var item in msg.Items)
        {
            var product = await uow.Products.GetByIdAsync(item.ProductId, context.CancellationToken);
            if (product is null || product.StockQuantity < item.Quantity)
                insufficientItems.Add(item.Sku);
        }

        if (insufficientItems.Count > 0)
        {
            var reason = $"Insufficient stock for: {string.Join(", ", insufficientItems)}";
            logger.LogWarning("Stock reservation failed for OrderId={OrderId}. {Reason}", msg.OrderId, reason);
            await context.Publish(new StockReservationFailedIntegrationEvent(msg.OrderId, msg.UserId, reason));
            return;
        }

        // Apply decrements
        foreach (var item in msg.Items)
        {
            var product = await uow.Products.GetByIdAsync(item.ProductId, context.CancellationToken);
            if (product is null) continue;
            product.DecrementStock(item.Quantity);
            await uow.Products.UpdateAsync(product, context.CancellationToken);
        }
        await uow.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Stock reserved for OrderId={OrderId}", msg.OrderId);
        await context.Publish(new StockReservedIntegrationEvent(msg.OrderId, msg.UserId));
    }
}
