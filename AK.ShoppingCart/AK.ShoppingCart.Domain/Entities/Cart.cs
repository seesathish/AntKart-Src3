using AK.ShoppingCart.Domain.Events;

namespace AK.ShoppingCart.Domain.Entities;

public sealed class Cart
{
    private readonly List<CartItem> _items = [];
    private readonly List<object> _domainEvents = [];

    public string UserId { get; private set; } = string.Empty;
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public decimal TotalAmount => _items.Sum(i => i.Price * i.Quantity);
    public int TotalItems => _items.Sum(i => i.Quantity);
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    private Cart() { }

    public static Cart Create(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId cannot be empty", nameof(userId));

        return new Cart
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Cart Restore(string userId, DateTime createdAt, DateTime updatedAt, IEnumerable<CartItem> items)
    {
        var cart = new Cart { UserId = userId, CreatedAt = createdAt, UpdatedAt = updatedAt };
        cart._items.AddRange(items);
        return cart;
    }

    public void AddItem(string productId, string productName, string sku, decimal price, int quantity, string? imageUrl = null)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
            existing.UpdateQuantity(existing.Quantity + quantity);
        else
            _items.Add(CartItem.Create(productId, productName, sku, price, quantity, imageUrl));

        SetUpdated();
        _domainEvents.Add(new CartItemAddedEvent(UserId, productId, quantity));
    }

    public void RemoveItem(string productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Product '{productId}' not found in cart");

        _items.Remove(item);
        SetUpdated();
        _domainEvents.Add(new CartItemRemovedEvent(UserId, productId));
    }

    public void UpdateItemQuantity(string productId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException($"Product '{productId}' not found in cart");

        if (quantity <= 0)
            _items.Remove(item);
        else
            item.UpdateQuantity(quantity);

        SetUpdated();
    }

    public void Clear()
    {
        _items.Clear();
        SetUpdated();
        _domainEvents.Add(new CartClearedEvent(UserId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void SetUpdated() => UpdatedAt = DateTime.UtcNow;
}
