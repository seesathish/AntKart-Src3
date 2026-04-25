using AK.Order.Domain.Common;
using AK.Order.Domain.Enums;
using AK.Order.Domain.Events;
using AK.Order.Domain.ValueObjects;

namespace AK.Order.Domain.Entities;

public sealed class Order : Entity, IAggregateRoot
{
    private readonly List<OrderItem> _items = [];

    public string OrderNumber { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public string? Notes { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    public decimal TotalAmount => _items.Sum(i => i.Price * i.Quantity);
    public int TotalItems => _items.Sum(i => i.Quantity);

    private Order() { }

    public static Order Create(
        string userId,
        ShippingAddress shippingAddress,
        IEnumerable<OrderItem> items,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        ArgumentNullException.ThrowIfNull(shippingAddress);

        var itemList = items.ToList();
        if (itemList.Count == 0)
            throw new ArgumentException("Order must contain at least one item.", nameof(items));

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(),
            UserId = userId.Trim(),
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Notes = notes?.Trim()
        };

        order._items.AddRange(itemList);
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, order.UserId, order.OrderNumber));
        return order;
    }

    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> _allowedTransitions = new()
    {
        [OrderStatus.Pending]       = [OrderStatus.Confirmed, OrderStatus.Cancelled, OrderStatus.PaymentFailed],
        [OrderStatus.Confirmed]     = [OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Processing]    = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped]       = [OrderStatus.Delivered],
        [OrderStatus.Delivered]     = [],
        [OrderStatus.Cancelled]     = [],
        [OrderStatus.Paid]          = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Pending, OrderStatus.Cancelled],
    };

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (newStatus == Status) return;

        if (!_allowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException(
                $"Cannot transition order from {Status} to {newStatus}.");

        var oldStatus = Status;
        Status = newStatus;
        SetUpdatedAt();
        AddDomainEvent(new OrderStatusChangedEvent(Id, oldStatus, newStatus));
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");
        if (Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Cannot cancel a delivered order.");

        Status = OrderStatus.Cancelled;
        SetUpdatedAt();
        AddDomainEvent(new OrderCancelledEvent(Id, UserId));
    }

    public void ConfirmPayment()
    {
        PaymentStatus = PaymentStatus.Paid;
        SetUpdatedAt();
    }

    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var existing = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existing is not null)
            existing.IncrementQuantity(item.Quantity);
        else
            _items.Add(item);

        SetUpdatedAt();
    }

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
}
