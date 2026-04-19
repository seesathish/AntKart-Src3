using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Commands.InitiatePayment;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;
using AK.Payments.Domain.Enums;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class InitiatePaymentCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly Mock<IConfiguration> _config = new();

    public InitiatePaymentCommandHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
        _razorpay.Setup(r => r.CreateOrderAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RazorpayOrderResponse("order_test123", "created", 99900L, "INR", "receipt"));
        _config.Setup(c => c["Razorpay:KeyId"]).Returns("rzp_test_key");
    }

    private InitiatePaymentCommandHandler CreateHandler()
        => new(_uow.Object, _razorpay.Object, _publisher.Object, _config.Object);

    [Fact]
    public async Task Handle_WithValidCommand_CreatesPaymentAndReturnsResponse()
    {
        var handler = CreateHandler();
        var command = new InitiatePaymentCommand(PaymentTestDataFactory.OrderId1, "user1", 999m, PaymentMethod.Card);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.RazorpayOrderId.Should().Be("order_test123");
        result.RazorpayKeyId.Should().Be("rzp_test_key");
        result.Amount.Should().Be(999m);
        result.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task Handle_PublishesPaymentInitiatedIntegrationEvent()
    {
        var handler = CreateHandler();
        var command = new InitiatePaymentCommand(PaymentTestDataFactory.OrderId1, "user1", 999m, PaymentMethod.Card);

        await handler.Handle(command, CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<PaymentInitiatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsRazorpayOrderIdOnPayment()
    {
        Payment? captured = null;
        _payments.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment, CancellationToken>((p, _) => captured = p);

        var handler = CreateHandler();
        var command = new InitiatePaymentCommand(PaymentTestDataFactory.OrderId1, "user1", 999m, PaymentMethod.Card);
        await handler.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RazorpayOrderId.Should().Be("order_test123");
        captured.Status.Should().Be(PaymentStatus.Initiated);
    }

    [Fact]
    public async Task Handle_CallsRazorpayCreateOrderWithCorrectAmount()
    {
        var handler = CreateHandler();
        var command = new InitiatePaymentCommand(PaymentTestDataFactory.OrderId1, "user1", 500m, PaymentMethod.Card);

        await handler.Handle(command, CancellationToken.None);

        _razorpay.Verify(r => r.CreateOrderAsync(500m, "INR", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesChanges()
    {
        var handler = CreateHandler();
        var command = new InitiatePaymentCommand(PaymentTestDataFactory.OrderId1, "user1", 999m, PaymentMethod.Card);

        await handler.Handle(command, CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
