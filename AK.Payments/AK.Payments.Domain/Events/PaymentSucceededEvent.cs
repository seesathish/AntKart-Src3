using AK.Payments.Domain.Common;

namespace AK.Payments.Domain.Events;

public sealed record PaymentSucceededEvent(Guid PaymentId, Guid OrderId, string RazorpayPaymentId) : IDomainEvent;
