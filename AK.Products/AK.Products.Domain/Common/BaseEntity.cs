namespace AK.Products.Domain.Common;

public abstract class BaseEntity
{
    public string Id { get; protected set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    protected void SetUpdated() => UpdatedAt = DateTime.UtcNow;
}
