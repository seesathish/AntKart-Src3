using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Enums;
using MediatR;

namespace AK.Payments.Application.Commands.InitiatePayment;

public sealed record InitiatePaymentCommand(
    Guid OrderId,
    string UserId,
    decimal Amount,
    PaymentMethod Method,
    string? SavedCardToken = null,
    string? CustomerEmail = null,
    string? CustomerContact = null) : IRequest<InitiatePaymentResponse>;
