namespace AK.Order.Domain.Common;

// Base class for all domain entities. Every entity gets a unique Id and audit timestamps
// automatically — subclasses never set these manually.
//
// Domain events: when something meaningful happens inside an entity (order placed, cancelled),
// the entity records that as a domain event. The UnitOfWork dispatches these events AFTER
// saving to the database, ensuring they are only published for changes that actually persisted.
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    // Exposed as read-only so external code can read events but never modify the list directly.
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    // Called by the UnitOfWork after events have been dispatched, to prevent replaying them.
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;
}
