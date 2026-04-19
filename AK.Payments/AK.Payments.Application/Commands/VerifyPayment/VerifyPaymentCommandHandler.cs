using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MassTransit;
using MediatR;

namespace AK.Payments.Application.Commands.VerifyPayment;

public sealed class VerifyPaymentCommandHandler(IUnitOfWork uow, IRazorpayClient razorpay, IPublishEndpoint publisher)
    : IRequestHandler<VerifyPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(VerifyPaymentCommand request, CancellationToken ct)
    {
        var payment = await uow.Payments.GetByIdAsync(request.PaymentId, ct)
            ?? throw new KeyNotFoundException($"Payment {request.PaymentId} not found.");

        var isValid = razorpay.VerifyPaymentSignature(
            request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature);

        if (isValid)
        {
            payment.MarkSucceeded(request.RazorpayPaymentId, request.RazorpaySignature);
            var succeededEvt = new PaymentSucceededIntegrationEvent(
                payment.Id, payment.OrderId, payment.UserId, request.RazorpayPaymentId);
            await publisher.Publish(succeededEvt, ct);
        }
        else
        {
            payment.MarkFailed("Signature verification failed.");
            var failedEvt = new PaymentFailedIntegrationEvent(
                payment.Id, payment.OrderId, payment.UserId, "Signature verification failed.");
            await publisher.Publish(failedEvt, ct);
        }

        await uow.SaveChangesAsync(ct);
        payment.ClearDomainEvents();

        return PaymentMapper.ToDto(payment);
    }
}
