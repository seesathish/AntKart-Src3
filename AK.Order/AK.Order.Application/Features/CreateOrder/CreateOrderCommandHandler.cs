using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Entities;
using AK.Order.Domain.ValueObjects;
using MassTransit;
using MediatR;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Application.Features.CreateOrder;

// Handles order creation: builds the domain aggregate, persists it, and kicks off the SAGA
// by publishing OrderCreatedIntegrationEvent to the message bus.
//
// Security: UserId, CustomerEmail, and CustomerName come from the command (injected from JWT
// at the endpoint layer) — never from the client request body.
//
// SAGA trigger: publishing OrderCreatedIntegrationEvent starts the OrderSaga in AK.Order,
// which waits for AK.Products to confirm stock reservation before confirming or cancelling.
public sealed class CreateOrderCommandHandler(
    IUnitOfWork uow,
    IPublishEndpoint publisher,
    IEventGridSideEffectPublisher sideEffects)
    : IRequestHandler<CreateOrderCommand, OrderDto>
{
    // Order totals are in the catalogue currency (USD); the Order aggregate doesn't store a currency.
    private const string OrderCurrency = "USD";

    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var addr = request.Order.ShippingAddress;

        // ShippingAddress is a Value Object — created via factory which validates required fields.
        var shippingAddress = ShippingAddress.Create(
            addr.FullName, addr.AddressLine1, addr.AddressLine2,
            addr.City, addr.State, addr.PostalCode, addr.Country, addr.Phone);

        var items = request.Order.Items.Select(i =>
            OrderItem.Create(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl)).ToList();

        // Order.Create validates invariants, generates the order number, sets status to Pending,
        // and raises an OrderCreatedEvent (domain event, dispatched after SaveChangesAsync).
        var order = OrderEntity.Create(request.UserId, request.CustomerEmail, request.CustomerName, shippingAddress, items, request.Order.Notes);
        await uow.Orders.AddAsync(order, ct);

        // Publish integration event to the message bus — this starts the SAGA in OrderSaga.
        // OrderItemPayload carries the product IDs and quantities needed by ReserveStockConsumer.
        var integrationEvent = new OrderCreatedIntegrationEvent(
            order.Id,
            order.UserId,
            order.CustomerEmail,
            order.CustomerName,
            order.OrderNumber,
            items.Select(i => new OrderItemPayload(i.ProductId, i.SKU, i.Quantity, i.Price)).ToList(),
            order.TotalAmount);

        await publisher.Publish(integrationEvent, ct);

        // SaveChangesAsync commits both the order row and the outbox message in one transaction.
        await uow.SaveChangesAsync(ct);
        order.ClearDomainEvents();

        // COMMIT-THEN-NOTIFY. The order + outbox are now durably committed. Only now do we emit the
        // customer notification as a FIRE-AND-FORGET Event Grid side-effect — strictly AFTER the
        // commit, never inside the business transaction. TryPublishAsync NEVER throws, so a
        // notification failure cannot fail this handler or roll back the created order. (A serverless
        // Function consumes the event and sends the email; that path is fully decoupled from here.)
        await sideEffects.TryPublishAsync(
            NotificationEventTypes.OrderCreated,
            $"orders/{order.Id}",
            new OrderCreatedNotification(
                order.CustomerEmail, order.CustomerName, order.OrderNumber,
                order.TotalAmount, OrderCurrency, DateTimeOffset.UtcNow),
            ct);

        return OrderMapper.ToDto(order);
    }
}
