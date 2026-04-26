namespace AK.Payments.Domain.Common;

// Base class for all Payments domain entities.
// Domain events are typed to IDomainEvent (not object) to catch type errors at compile time.
// The UnitOfWork dispatches events after SaveChangesAsync, then calls ClearDomainEvents()
// to prevent replaying them on the next save.
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void SetUpdatedAt() => UpdatedAt = DateTimeOffset.UtcNow;
}
