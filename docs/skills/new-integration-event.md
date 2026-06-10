# Skill: Add a New Integration Event

**Purpose:** Add a new integration event that flows between two AntKart services over Azure Service Bus (via MassTransit) — event contract definition in BuildingBlocks, publisher wiring in the source service, consumer class and registration in the target service, and a MassTransit in-memory integration test.

---

## When to Use
- A business action in Service A needs to trigger side effects in Service B or C
- Examples: `OrderShippedIntegrationEvent` → Notification sends shipping email; `ReviewPostedIntegrationEvent` → Products updates average rating

## Prerequisite Reading
- [EVENTBUS.md](../design/EVENTBUS.md) — fan-out topology (integration-events topic + subscriptions), receive-endpoint naming, IaC-owned topology, SAGA pattern
- [AK.BuildingBlocks/Messaging/IntegrationEvents/](../../AK.BuildingBlocks/AK.BuildingBlocks/Messaging/IntegrationEvents/) — existing event records

---

## Step 1 — Define the Event Contract in BuildingBlocks

All integration events live in `AK.BuildingBlocks/AK.BuildingBlocks/Messaging/IntegrationEvents/`. Never define them inside a service project — this guarantees publisher and consumer share the same type.

```csharp
// AK.BuildingBlocks/AK.BuildingBlocks/Messaging/IntegrationEvents/OrderShippedIntegrationEvent.cs
namespace AK.BuildingBlocks.Messaging.IntegrationEvents;

public record OrderShippedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    string CustomerId,
    string CustomerEmail,
    string CustomerName,
    string TrackingNumber,
    string CourierName,
    DateTimeOffset ShippedAt) : IIntegrationEvent;
```

**Rules:**
- `record` type — value equality, immutable
- Implements `IIntegrationEvent` (marker interface from BuildingBlocks)
- Include all data the consumer needs — consumers must never call back to the publisher's API to fetch more data (no chatty coupling)
- Use `string` for IDs sent across service boundaries — avoids GUID serialization mismatches
- Prefix with the source domain: `Order`, `Payment`, `User`, etc.

---

## Step 2 — Publish the Event from the Source Service

**2a — Add `IPublishEndpoint` to the handler:**

```csharp
// AK.Order/AK.Order.Application/Features/UpdateStatus/UpdateOrderStatusCommandHandler.cs
internal sealed class UpdateOrderStatusCommandHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint) : IRequestHandler<UpdateOrderStatusCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(UpdateOrderStatusCommand request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct)
            ?? throw new KeyNotFoundException($"Order {request.OrderId} not found.");

        order.UpdateStatus(request.NewStatus);
        await unitOfWork.SaveChangesAsync(ct);

        if (request.NewStatus == OrderStatus.Shipped)
        {
            await publishEndpoint.Publish(new OrderShippedIntegrationEvent(
                order.Id.ToString(),
                order.OrderNumber,
                order.UserId,
                order.CustomerEmail,
                order.CustomerName,
                request.TrackingNumber,
                request.CourierName,
                DateTimeOffset.UtcNow), ct);
        }

        return Result<OrderDto>.Success(order.ToDto());
    }
}
```

**2b — Confirm MassTransit is already registered in the source service's Infrastructure:**
```csharp
// AK.Order/AK.Order.Infrastructure/Extensions/ServiceCollectionExtensions.cs
services.AddAzureServiceBusMassTransit(configuration, "order", configure =>
{
    // existing consumers...
    // no action needed for publishing — IPublishEndpoint is auto-registered by MassTransit
});
```

Publishing requires no additional registration. `IPublishEndpoint` is resolved from DI automatically when `AddAzureServiceBusMassTransit` is called.

---

## Step 3 — Create the Consumer in the Target Service

**3a — Consumer class** in `AK.<Target>.Infrastructure/Consumers/` or `AK.<Target>.Infrastructure/EventBus/`:

```csharp
// AK.Notification/AK.Notification.Infrastructure/Consumers/OrderShippedConsumer.cs
public class OrderShippedConsumer(IMediator mediator, ILogger<OrderShippedConsumer> logger)
    : IConsumer<OrderShippedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderShippedIntegrationEvent> context)
    {
        var evt = context.Message;
        logger.LogInformation("Consumed OrderShippedIntegrationEvent for order {OrderNumber}", evt.OrderNumber);

        await mediator.Send(new SendNotificationCommand(
            evt.CustomerId,
            evt.CustomerEmail,
            NotificationType.OrderShipped,
            new { evt.OrderNumber, evt.TrackingNumber, evt.CourierName }));
    }
}
```

**Rules:**
- Implement `IConsumer<TIntegrationEvent>` from MassTransit
- Delegate business logic to a MediatR command — keep the consumer thin
- Log the event receipt with a correlation identifier (OrderId, OrderNumber, etc.)
- Do not throw from `Consume` unless you want MassTransit to retry — catch and handle expected failures

**3b — Register the consumer in the target service's MassTransit configuration:**

```csharp
// AK.Notification/AK.Notification.Infrastructure/Extensions/ServiceCollectionExtensions.cs
services.AddAzureServiceBusMassTransit(configuration, "notification", configure =>
{
    configure.AddConsumer<OrderCreatedConsumer>();
    configure.AddConsumer<OrderConfirmedConsumer>();
    configure.AddConsumer<OrderCancelledConsumer>();
    configure.AddConsumer<PaymentSucceededConsumer>();
    configure.AddConsumer<PaymentFailedConsumer>();
    configure.AddConsumer<OrderShippedConsumer>();   // ← add here
});
```

The `AddAzureServiceBusMassTransit` helper in BuildingBlocks uses the service prefix (`"notification"`) to give the consumer a uniquely-named receive endpoint (`notification-order-shipped`). The event is delivered through the service's **subscription** on the `integration-events` topic, so every interested service receives its own copy independently (fan-out).

> **Topology is owned by infrastructure-as-code.** If the target service already has a subscription on the `integration-events` topic, no new entity is needed — the new event simply reaches its newly-registered consumer. If the event is delivered through a **new** entity (a first subscription for a service, or a new command **queue**), that entity must be **added in infrastructure-as-code** — the application's identity is Send/Receive-only and never creates topology at runtime.

---

## Step 4 — Update the Events Map in docs/design/EVENTBUS.md

In `docs/design/EVENTBUS.md`, add the new event to the **Events and their consumers** table (publisher and consuming services):

```markdown
| `OrderShippedIntegrationEvent` | AK.Order (on status → Shipped) | AK.Notification |
```

If the new event required a new Service Bus entity (a first subscription for a service, or a new command queue), record that it is provisioned in **infrastructure-as-code**, not by the application.

---

## Step 5 — Write an Integration Test

In `AK.IntegrationTests/`, add a test file `OrderShippedConsumerTests.cs` using the MassTransit in-memory test harness. The harness is transport-agnostic — no broker or DB needed.

```csharp
public class OrderShippedConsumerTests
{
    [Fact]
    public async Task OrderShippedConsumer_WhenEventPublished_SendsNotification()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderShippedConsumer>())
            .AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SendNotificationCommand).Assembly))
            // add other mocked dependencies
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evt = new OrderShippedIntegrationEvent(
            Guid.NewGuid().ToString(),
            "ORD-20260517-ABCD1234",
            Guid.NewGuid().ToString(),
            "test@example.com",
            "Test User",
            "TRACK123",
            "BlueDart",
            DateTimeOffset.UtcNow);

        await harness.Bus.Publish(evt);

        var consumerHarness = harness.GetConsumerHarness<OrderShippedConsumer>();
        (await consumerHarness.Consumed.Any<OrderShippedIntegrationEvent>()).Should().BeTrue();
    }
}
```

---

## Step 6 — Build and Test

```bash
dotnet build          # 0 errors
dotnet test           # all pass; new integration test included
```

---

## Step 7 — Update Documentation

- `docs/design/EVENTBUS.md` — Events-and-their-consumers table updated (Step 4)
- Source service `<SERVICE>_TECHNICAL_DESIGN.md` — add event to "Integration Events Published" table
- Target service `<SERVICE>_TECHNICAL_DESIGN.md` — add event to "Events Consumed" table
- `CLAUDE.md` — update the BuildingBlocks `Messaging/IntegrationEvents/` list

---

## Checklist

- [ ] Event record defined in BuildingBlocks `IntegrationEvents/` (implements `IIntegrationEvent`)
- [ ] Event contains all data consumers need (no callback required)
- [ ] `IPublishEndpoint.Publish(...)` called at correct point in source handler
- [ ] Consumer class created in target service Infrastructure (`IConsumer<T>` implemented)
- [ ] Consumer registered in target service `AddAzureServiceBusMassTransit` configure callback
- [ ] If a new subscription/queue was needed, it was added in infrastructure-as-code
- [ ] MassTransit in-memory integration test written and passing
- [ ] `docs/design/EVENTBUS.md` Events-and-their-consumers table updated
- [ ] Source service design doc "Published" table updated
- [ ] Target service design doc "Consumed" table updated
- [ ] BuildingBlocks section in `CLAUDE.md` updated
