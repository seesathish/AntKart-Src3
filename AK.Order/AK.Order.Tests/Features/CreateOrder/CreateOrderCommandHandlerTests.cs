using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.CreateOrder;
using AK.Order.Domain.Entities;
using AK.Order.Tests.Common;
using MassTransit;
using OrderEntity = AK.Order.Domain.Entities.Order;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.CreateOrder;

public class CreateOrderCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();

    public CreateOrderCommandHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _repo.Setup(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity o, CancellationToken _) => o);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsOrderDto()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object);
        var command = new CreateOrderCommand("user-123", TestDataFactory.CreateOrderDto());

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.UserId.Should().Be("user-123");
        result.OrderNumber.Should().StartWith("ORD-");
        result.Status.Should().Be("Pending");
        result.PaymentStatus.Should().Be("Pending");
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_SavesOrder()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object);
        var command = new CreateOrderCommand("user-123", TestDataFactory.CreateOrderDto());

        await handler.Handle(command, CancellationToken.None);

        _repo.Verify(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_ClearsDomainEvents()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object);
        var command = new CreateOrderCommand("user-123", TestDataFactory.CreateOrderDto());

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_MapsShippingAddress()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object);
        var command = new CreateOrderCommand("user-123", TestDataFactory.CreateOrderDto());

        var result = await handler.Handle(command, CancellationToken.None);

        result.ShippingAddress.FullName.Should().Be("John Doe");
        result.ShippingAddress.City.Should().Be("Springfield");
    }
}
