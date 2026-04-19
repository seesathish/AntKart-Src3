using AK.Products.Domain.Common;
using AK.Products.Domain.Enums;
using AK.Products.Domain.Events;
using AK.Products.Domain.ValueObjects;
using MongoDB.Bson.Serialization.Attributes;

namespace AK.Products.Domain.Entities;

public sealed class Product : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SKU { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public ProductStatus Status { get; private set; }
    public string CategoryName { get; private set; } = string.Empty;
    public string? SubCategoryName { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";
    public decimal? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public List<string> Sizes { get; private set; } = new();
    public List<string> Colors { get; private set; } = new();
    public List<string> ImageUrls { get; private set; } = new();
    public string? Material { get; private set; }
    public bool IsFeatured { get; private set; }
    public double Rating { get; private set; }
    public int ReviewCount { get; private set; }
    public List<string> Tags { get; private set; } = new();

    [BsonIgnore]
    private readonly List<IDomainEvent> _domainEvents = new();

    [BsonIgnore]
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Product() { }

    public static Product Create(
        string name, string description, string sku, string brand,
        string categoryName, string? subCategoryName,
        decimal price, string currency, int stockQuantity,
        List<string> sizes, List<string> colors, string? material = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(categoryName)) throw new ArgumentException("CategoryName is required.", nameof(categoryName));

        var product = new Product
        {
            Name = name,
            Description = description,
            SKU = sku,
            Brand = brand,
            CategoryName = categoryName,
            SubCategoryName = subCategoryName,
            Price = price,
            Currency = currency,
            StockQuantity = stockQuantity,
            Status = stockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock,
            Sizes = sizes,
            Colors = colors,
            Material = material
        };
        product._domainEvents.Add(new ProductCreatedEvent(product.Id, product.Name));
        return product;
    }

    public void Update(string name, string description, string brand, decimal price, int stockQuantity, string? material)
    {
        Name = name;
        Description = description;
        Brand = brand;
        Price = price;
        StockQuantity = stockQuantity;
        Material = material;
        Status = stockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock;
        SetUpdated();
        _domainEvents.Add(new ProductUpdatedEvent(Id, Name));
    }

    public void SetDiscount(decimal discountPrice)
    {
        if (discountPrice >= Price) throw new InvalidOperationException("Discount price must be less than original price");
        DiscountPrice = discountPrice;
        SetUpdated();
    }

    public void RemoveDiscount() { DiscountPrice = null; SetUpdated(); }

    public void SetFeatured(bool isFeatured) { IsFeatured = isFeatured; SetUpdated(); }

    public void AddReview(double rating)
    {
        Rating = ((Rating * ReviewCount) + rating) / (ReviewCount + 1);
        ReviewCount++;
        SetUpdated();
    }

    public void DecrementStock(int quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (StockQuantity < quantity)
            throw new InvalidOperationException(
                $"Insufficient stock for SKU '{SKU}'. Available: {StockQuantity}, Requested: {quantity}");
        StockQuantity -= quantity;
        Status = StockQuantity > 0 ? ProductStatus.Active : ProductStatus.OutOfStock;
        SetUpdated();
    }

    public void Deactivate() { Status = ProductStatus.Inactive; SetUpdated(); }

    public void ClearDomainEvents() => _domainEvents.Clear();

    public Money GetPrice() => new(Price, Currency);
    public Money? GetDiscountPrice() => DiscountPrice.HasValue ? new(DiscountPrice.Value, Currency) : null;
}
