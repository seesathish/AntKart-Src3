# Skill: Add a New Integration Event

**Purpose:** Add a new integration event that flows between two AntKart services via RabbitMQ/MassTransit — event contract definition in BuildingBlocks, publisher wiring in the source service, consumer class and registration in the target service, and a MassTransit in-memory integration test.

---

## When to Use
- A business action in Service A needs to trigger side effects in Service B or C
- Examples: `OrderShippedIntegrationEvent` → Notification sends shipping email; `ReviewPostedIntegrationEvent` → Products updates average rating

## Prerequisite Reading
- [EVENTBUS.md](../../EVENTBUS.md) — fan-out exchange topology, queue naming, SAGA pattern
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
services.AddRabbitMqMassTransit(configuration, "order", configure =>
{
    // existing consumers...
    // no action needed for publishing — IPublishEndpoint is auto-registered by MassTransit
});
```

Publishing requires no additional registration. `IPublishEndpoint` is resolved from DI automatically when `AddRabbitMqMassTransit` is called.

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
services.AddRabbitMqMassTransit(configuration, "notification", configure =>
{
    configure.AddConsumer<OrderCreatedConsumer>();
    configure.AddConsumer<OrderConfirmedConsumer>();
    configure.AddConsumer<OrderCancelledConsumer>();
    configure.AddConsumer<PaymentSucceededConsumer>();
    configure.AddConsumer<PaymentFailedConsumer>();
    configure.AddConsumer<UserRegisteredConsumer>();
    configure.AddConsumer<OrderShippedConsumer>();   // ← add here
});
```

The `AddRabbitMqMassTransit` helper in BuildingBlocks uses the service prefix (`"notification"`) to create a uniquely-named queue `notification-order-shipped`. This ensures fan-out — both `notification-order-shipped` and any other service's queue (e.g. `analytics-order-shipped`) receive the same event independently.

---

## Step 4 — Update the RabbitMQ Exchange/Queue Map in EVENTBUS.md

In `EVENTBUS.md`, add a row to the Exchanges table and a row to the Queues table:

```markdown
| `OrderShippedIntegrationEvent` | `order-shipped` (fanout) | Published by AK.Order on status→Shipped |

| `notification-order-shipped` | `OrderShippedIntegrationEvent` | `OrderShippedConsumer` in AK.Notification |
```

---

## Step 5 — Write an Integration Test

In `AK.IntegrationTests/`, add a test file `OrderShippedConsumerTests.cs` using the MassTransit in-memory test harness. No real RabbitMQ or DB needed.

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

- `EVENTBUS.md` — Exchanges + Queues tables updated (Step 4)
- Source service `<SERVICE>_TECHNICAL_DESIGN.md` — add event to "Integration Events Published" table
- Target service `<SERVICE>_TECHNICAL_DESIGN.md` — add event to "Events Consumed" table
- `CLAUDE.md` — update the BuildingBlocks `Messaging/IntegrationEvents/` list

---

## Checklist

- [ ] Event record defined in BuildingBlocks `IntegrationEvents/` (implements `IIntegrationEvent`)
- [ ] Event contains all data consumers need (no callback required)
- [ ] `IPublishEndpoint.Publish(...)` called at correct point in source handler
- [ ] Consumer class created in target service Infrastructure (`IConsumer<T>` implemented)
- [ ] Consumer registered in target service `AddRabbitMqMassTransit` configure callback
- [ ] MassTransit in-memory integration test written and passing
- [ ] `EVENTBUS.md` Exchanges + Queues tables updated
- [ ] Source service design doc "Published" table updated
- [ ] Target service design doc "Consumed" table updated
- [ ] BuildingBlocks section in `CLAUDE.md` updated
