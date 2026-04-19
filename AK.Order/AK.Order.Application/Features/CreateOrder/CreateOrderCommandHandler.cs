using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Common.Mapping;
using AK.Order.Application.Common.DTOs;
using AK.Order.Domain.Entities;
using AK.Order.Domain.ValueObjects;
using MassTransit;
using MediatR;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Application.Features.CreateOrder;

public sealed class CreateOrderCommandHandler(IUnitOfWork uow, IPublishEndpoint publisher)
    : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var addr = request.Order.ShippingAddress;
        var shippingAddress = ShippingAddress.Create(
            addr.FullName, addr.AddressLine1, addr.AddressLine2,
            addr.City, addr.State, addr.PostalCode, addr.Country, addr.Phone);

        var items = request.Order.Items.Select(i =>
            OrderItem.Create(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl)).ToList();

        var order = OrderEntity.Create(request.UserId, shippingAddress, items, request.Order.Notes);
        await uow.Orders.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        order.ClearDomainEvents();

        var integrationEvent = new OrderCreatedIntegrationEvent(
            order.Id,
            order.UserId,
            items.Select(i => new OrderItemPayload(i.ProductId, i.SKU, i.Quantity, i.Price)).ToList(),
            order.TotalAmount);

        await publisher.Publish(integrationEvent, ct);

        return OrderMapper.ToDto(order);
    }
}
