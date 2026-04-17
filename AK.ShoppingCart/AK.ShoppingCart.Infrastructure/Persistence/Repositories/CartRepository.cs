using System.Text.Json;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AK.ShoppingCart.Infrastructure.Persistence.Repositories;

public sealed class CartRepository : ICartRepository
{
    private readonly IDatabase _db;
    private readonly RedisSettings _settings;

    private sealed record CartSnapshot(string UserId, DateTime CreatedAt, DateTime UpdatedAt, List<CartItemSnapshot> Items);
    private sealed record CartItemSnapshot(string ProductId, string ProductName, string SKU, decimal Price, int Quantity, string? ImageUrl);

    public CartRepository(RedisContext context, IOptions<RedisSettings> settings)
    {
        _db = context.GetDatabase();
        _settings = settings.Value;
    }

    public CartRepository(IDatabase database, IOptions<RedisSettings> settings)
    {
        _db = database;
        _settings = settings.Value;
    }

    private string GetKey(string userId) => $"{_settings.InstanceName}cart:{userId}";

    public async Task<Cart?> GetAsync(string userId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(GetKey(userId));
        if (value.IsNullOrEmpty) return null;

        var snapshot = JsonSerializer.Deserialize<CartSnapshot>(value!);
        if (snapshot is null) return null;

        var items = snapshot.Items.Select(i =>
            CartItem.Restore(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl));
        return Cart.Restore(snapshot.UserId, snapshot.CreatedAt, snapshot.UpdatedAt, items);
    }

    public async Task SaveAsync(Cart cart, CancellationToken ct = default)
    {
        var snapshot = new CartSnapshot(
            cart.UserId,
            cart.CreatedAt,
            cart.UpdatedAt,
            cart.Items.Select(i => new CartItemSnapshot(i.ProductId, i.ProductName, i.SKU, i.Price, i.Quantity, i.ImageUrl)).ToList());

        var json = JsonSerializer.Serialize(snapshot);
        await _db.StringSetAsync(GetKey(cart.UserId), json, TimeSpan.FromDays(_settings.CartExpiryDays));
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        await _db.KeyDeleteAsync(GetKey(userId));
    }

    public async Task<bool> ExistsAsync(string userId, CancellationToken ct = default)
    {
        return await _db.KeyExistsAsync(GetKey(userId));
    }
}
