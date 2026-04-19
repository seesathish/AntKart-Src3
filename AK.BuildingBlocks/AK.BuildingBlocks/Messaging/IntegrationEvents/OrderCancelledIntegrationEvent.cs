namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
