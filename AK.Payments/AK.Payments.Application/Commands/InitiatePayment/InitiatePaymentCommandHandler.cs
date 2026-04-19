using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AK.Payments.Application.Commands.InitiatePayment;

public sealed class InitiatePaymentCommandHandler(
    IUnitOfWork uow,
    IRazorpayClient razorpay,
    IPublishEndpoint publisher,
    IConfiguration configuration)
    : IRequestHandler<InitiatePaymentCommand, InitiatePaymentResponse>
{
    public async Task<InitiatePaymentResponse> Handle(InitiatePaymentCommand request, CancellationToken ct)
    {
        var payment = Payment.Create(request.OrderId, request.UserId, request.Amount, request.Method, request.SavedCardToken);
        await uow.Payments.AddAsync(payment, ct);

        var rzpOrder = await razorpay.CreateOrderAsync(request.Amount, "INR", payment.Id.ToString(), ct);
        payment.AssignRazorpayOrder(rzpOrder.Id);

        var evt = new PaymentInitiatedIntegrationEvent(
            payment.Id, payment.OrderId, payment.UserId, payment.Amount, payment.Currency, rzpOrder.Id);
        await publisher.Publish(evt, ct);

        await uow.SaveChangesAsync(ct);
        payment.ClearDomainEvents();

        var keyId = configuration["Razorpay:KeyId"] ?? string.Empty;
        return new InitiatePaymentResponse(payment.Id, rzpOrder.Id, keyId, payment.Amount, payment.Currency);
    }
}
