using AK.Payments.Domain.Common;

namespace AK.Payments.Domain.Events;

public sealed record PaymentFailedEvent(Guid PaymentId, Guid OrderId, string Reason) : IDomainEvent;
