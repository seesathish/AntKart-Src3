namespace AK.Products.Domain.Common;

public abstract class Entity
{
    public string Id { get; protected set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; protected set; } = DateTime.UtcNow;

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;
}
