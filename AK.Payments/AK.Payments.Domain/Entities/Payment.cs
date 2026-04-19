using AK.Payments.Domain.Common;
using AK.Payments.Domain.Enums;
using AK.Payments.Domain.Events;

namespace AK.Payments.Domain.Entities;

public sealed class Payment : Entity, IAggregateRoot
{
    public Guid OrderId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "INR";
    public PaymentStatus Status { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? RazorpayOrderId { get; private set; }
    public string? RazorpayPaymentId { get; private set; }
    public string? RazorpaySignature { get; private set; }
    public string? FailureReason { get; private set; }
    public string? SavedCardToken { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid orderId,
        string userId,
        decimal amount,
        PaymentMethod method,
        string? savedCardToken = null)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        var payment = new Payment
        {
            OrderId = orderId,
            UserId = userId.Trim(),
            Amount = amount,
            Method = method,
            Status = PaymentStatus.Pending,
            SavedCardToken = savedCardToken
        };

        payment.AddDomainEvent(new PaymentCreatedEvent(payment.Id, orderId, userId));
        return payment;
    }

    public void AssignRazorpayOrder(string razorpayOrderId)
    {
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("RazorpayOrderId is required.", nameof(razorpayOrderId));

        RazorpayOrderId = razorpayOrderId;
        Status = PaymentStatus.Initiated;
        SetUpdatedAt();
    }

    public void MarkSucceeded(string razorpayPaymentId, string razorpaySignature)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("Payment is already succeeded.");
        if (Status == PaymentStatus.Failed)
            throw new InvalidOperationException("Cannot succeed a failed payment.");

        RazorpayPaymentId = razorpayPaymentId;
        RazorpaySignature = razorpaySignature;
        Status = PaymentStatus.Succeeded;
        SetUpdatedAt();
        AddDomainEvent(new PaymentSucceededEvent(Id, OrderId, razorpayPaymentId));
    }

    public void MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("Cannot fail a succeeded payment.");

        FailureReason = reason;
        Status = PaymentStatus.Failed;
        SetUpdatedAt();
        AddDomainEvent(new PaymentFailedEvent(Id, OrderId, reason));
    }
}
