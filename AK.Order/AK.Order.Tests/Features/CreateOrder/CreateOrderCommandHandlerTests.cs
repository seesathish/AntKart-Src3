using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.Notifications;
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
    private readonly Mock<IEventGridSideEffectPublisher> _sideEffects = new();

    public CreateOrderCommandHandlerTests()
    {
        _uow.Setup(u => u.Orders).Returns(_repo.Object);
        _repo.Setup(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity o, CancellationToken _) => o);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private CreateOrderCommand BuildCommand(
        string userId = "user-123",
        string email = "john@example.com",
        string name = "John Doe")
        => new(userId, email, name, TestDataFactory.CreateOrderDto());

    [Fact]
    public async Task Handle_ValidCommand_ReturnsOrderDto()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        var result = await handler.Handle(BuildCommand(), CancellationToken.None);

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
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        await handler.Handle(BuildCommand(), CancellationToken.None);

        _repo.Verify(r => r.AddAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_PublishesOrderCreatedEvent()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        await handler.Handle(BuildCommand("user-123", "john@example.com", "John Doe"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<AK.BuildingBlocks.Messaging.IntegrationEvents.OrderCreatedIntegrationEvent>(e =>
                e.UserId == "user-123" &&
                e.CustomerEmail == "john@example.com" &&
                e.CustomerName == "John Doe" &&
                e.OrderNumber.StartsWith("ORD-")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_MapsShippingAddress()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        var result = await handler.Handle(BuildCommand(), CancellationToken.None);

        result.ShippingAddress.FullName.Should().Be("John Doe");
        result.ShippingAddress.City.Should().Be("Springfield");
    }

    [Fact]
    public async Task Handle_ValidCommand_StoresCustomerEmail()
    {
        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        var result = await handler.Handle(BuildCommand(email: "customer@test.com"), CancellationToken.None);

        result.Should().NotBeNull();
        _publisher.Verify(p => p.Publish(
            It.Is<AK.BuildingBlocks.Messaging.IntegrationEvents.OrderCreatedIntegrationEvent>(e =>
                e.CustomerEmail == "customer@test.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AfterCommit_PublishesOrderCreatedNotificationSideEffect()
    {
        OrderCreatedNotification? published = null;
        _sideEffects.Setup(s => s.TryPublishAsync(
                NotificationEventTypes.OrderCreated, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object, CancellationToken>((_, _, data, _) => published = (OrderCreatedNotification)data)
            .ReturnsAsync(true);

        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        await handler.Handle(BuildCommand(email: "customer@test.com", name: "John Doe"), CancellationToken.None);

        // Commit happens, then the notification is published (after SaveChangesAsync).
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        published.Should().NotBeNull();
        published!.CustomerEmail.Should().Be("customer@test.com");
        published.CustomerName.Should().Be("John Doe");
        published.OrderNumber.Should().StartWith("ORD-");
    }

    [Fact]
    public async Task Handle_WhenNotificationPublishFails_DoesNotFailTheOrder()
    {
        // TryPublishAsync never throws; a publish failure surfaces as false. The order must still
        // be created and returned — the notification is a decoupled side-effect.
        _sideEffects.Setup(s => s.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateOrderCommandHandler(_uow.Object, _publisher.Object, _sideEffects.Object);
        var act = async () => await handler.Handle(BuildCommand(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.OrderNumber.Should().StartWith("ORD-");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
