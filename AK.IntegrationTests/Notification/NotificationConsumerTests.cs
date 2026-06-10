using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.IntegrationTests.Common;
using AK.Notification.Application.Commands;
using AK.Notification.Domain.Enums;
using FluentAssertions;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AK.IntegrationTests.Notification;

public sealed class NotificationConsumerTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private Mock<IMediator> _mediator = null!;

    public async Task InitializeAsync()
    {
        _mediator = new Mock<IMediator>();
        _mediator
            .Setup(m => m.Send(It.IsAny<SendNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _provider = TestHarnessFactory.CreateWithNotificationConsumers(_mediator);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task OrderCreated_ConsumerPublishesOrderConfirmationEmail()
    {
        var evt = IntegrationTestData.CreateOrderEvent();
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.Channel == NotificationChannel.Email &&
                c.TemplateType == NotificationTemplateType.OrderConfirmation &&
                c.RecipientAddress == IntegrationTestData.TestCustomerEmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrderConfirmed_ConsumerPublishesOrderConfirmedEmail()
    {
        var orderId = Guid.NewGuid();
        var evt = IntegrationTestData.CreateOrderConfirmedEvent(orderId);
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<OrderConfirmedIntegrationEvent>()).Should().BeTrue();
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.Channel == NotificationChannel.Email &&
                c.TemplateType == NotificationTemplateType.OrderConfirmed &&
                c.RecipientAddress == IntegrationTestData.TestCustomerEmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrderCancelled_ConsumerPublishesOrderCancelledEmail()
    {
        var orderId = Guid.NewGuid();
        var evt = IntegrationTestData.CreateOrderCancelledEvent(orderId, "Out of stock");
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<OrderCancelledIntegrationEvent>()).Should().BeTrue();
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.Channel == NotificationChannel.Email &&
                c.TemplateType == NotificationTemplateType.OrderCancelled &&
                c.RecipientAddress == IntegrationTestData.TestCustomerEmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentSucceeded_ConsumerPublishesPaymentSucceededEmail()
    {
        var evt = IntegrationTestData.CreatePaymentSucceededEvent();
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.Channel == NotificationChannel.Email &&
                c.TemplateType == NotificationTemplateType.PaymentSucceeded &&
                c.RecipientAddress == IntegrationTestData.TestCustomerEmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentFailed_ConsumerPublishesPaymentFailedEmail()
    {
        var evt = IntegrationTestData.CreatePaymentFailedEvent(reason: "Card declined");
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Consumed.Any<PaymentFailedIntegrationEvent>()).Should().BeTrue();
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c =>
                c.Channel == NotificationChannel.Email &&
                c.TemplateType == NotificationTemplateType.PaymentFailed &&
                c.RecipientAddress == IntegrationTestData.TestCustomerEmail),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultipleEvents_EachConsumerReceivesCorrectEvent()
    {
        var orderId = Guid.NewGuid();
        await _harness.Bus.Publish(IntegrationTestData.CreateOrderEvent(orderId));
        await _harness.Bus.Publish(IntegrationTestData.CreatePaymentSucceededEvent(orderId: orderId));
        await Task.Delay(600);

        (await _harness.Consumed.Any<OrderCreatedIntegrationEvent>()).Should().BeTrue();
        (await _harness.Consumed.Any<PaymentSucceededIntegrationEvent>()).Should().BeTrue();

        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c => c.TemplateType == NotificationTemplateType.OrderConfirmation),
            It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(
            It.Is<SendNotificationCommand>(c => c.TemplateType == NotificationTemplateType.PaymentSucceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
