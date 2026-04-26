namespace AK.Payments.Domain.Common;

// Marker interface for domain events raised inside Payments aggregate roots.
// The UnitOfWork dispatches these after saving to ensure events are only published
// for changes that actually persisted to the database.
public interface IDomainEvent { }
