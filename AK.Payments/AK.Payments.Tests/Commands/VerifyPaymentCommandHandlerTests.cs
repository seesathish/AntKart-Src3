using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Commands.VerifyPayment;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Domain.Enums;
using AK.Payments.Tests.TestData;
using FluentAssertions;
using MassTransit;
using Moq;

namespace AK.Payments.Tests.Commands;

public sealed class VerifyPaymentCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IRazorpayClient> _razorpay = new();
    private readonly Mock<IPublishEndpoint> _publisher = new();

    public VerifyPaymentCommandHandlerTests()
    {
        _uow.Setup(u => u.Payments).Returns(_payments.Object);
    }

    private VerifyPaymentCommandHandler CreateHandler()
        => new(_uow.Object, _razorpay.Object, _publisher.Object);

    [Fact]
    public async Task Handle_WithValidSignature_MarksPaymentSucceeded()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var handler = CreateHandler();
        var result = await handler.Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Succeeded.ToString());
        result.RazorpayPaymentId.Should().Be("pay_abc");
    }

    [Fact]
    public async Task Handle_WithInvalidSignature_MarksPaymentFailed()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = CreateHandler();
        var result = await handler.Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "bad_sig"), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed.ToString());
        result.FailureReason.Should().Contain("Signature");
    }

    [Fact]
    public async Task Handle_WithNonExistentPayment_ThrowsKeyNotFoundException()
    {
        _payments.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AK.Payments.Domain.Entities.Payment?)null);

        var handler = CreateHandler();
        var act = async () => await handler.Handle(new VerifyPaymentCommand(Guid.NewGuid(), "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_PublishesSucceededEventOnSuccess()
    {
        var payment = PaymentTestDataFactory.CreatePayment();
        payment.AssignRazorpayOrder("order_123");
        _payments.Setup(r => r.GetByIdAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        _razorpay.Setup(r => r.VerifyPaymentSignature(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var handler = CreateHandler();
        await handler.Handle(new VerifyPaymentCommand(payment.Id, "order_123", "pay_abc", "sig_abc"), CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<PaymentSucceededIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<PaymentFailedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
