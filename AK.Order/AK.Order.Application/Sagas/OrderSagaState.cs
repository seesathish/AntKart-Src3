using MassTransit;

namespace AK.Order.Application.Sagas;

public sealed class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public int Version { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
}
