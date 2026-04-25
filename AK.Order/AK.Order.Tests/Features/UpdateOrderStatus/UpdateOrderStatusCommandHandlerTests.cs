using AK.Order.Application.Common.Interfaces;
using AK.Order.Application.Features.UpdateOrderStatus;
using AK.Order.Domain.Entities;
using AK.Order.Domain.Enums;
using OrderEntity = AK.Order.Domain.Entities.Order;
using AK.Order.Tests.Common;
using FluentAssertions;
using Moq;

namespace AK.Order.Tests.Features.UpdateOrderStatus;

public class UpdateOrderStatusCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IOrderRepository> _repo = new();

    public UpdateOrderStatusCommandHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsUpdatedOrderDto()
    {
        var order = TestDataFactory.CreateOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new UpdateOrderStatusCommandHandler(_uow.Object);
        var result = await handler.Handle(new UpdateOrderStatusCommand(order.Id, OrderStatus.Confirmed), CancellationToken.None);

        result.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrderEntity?)null);

        var handler = new UpdateOrderStatusCommandHandler(_uow.Object);
        var act = async () => await handler.Handle(new UpdateOrderStatusCommand(Guid.NewGuid(), OrderStatus.Processing), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_CancelledOrder_ThrowsInvalidOperationException()
    {
        var order = TestDataFactory.CreateOrder();
        order.Cancel();
        _repo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = new UpdateOrderStatusCommandHandler(_uow.Object);
        var act = async () => await handler.Handle(new UpdateOrderStatusCommand(order.Id, OrderStatus.Processing), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
