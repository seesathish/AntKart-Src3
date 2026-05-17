# Skill: Add a New Consumer

**Purpose:** Add a MassTransit consumer to an existing AntKart service to handle an already-defined integration event from BuildingBlocks. Covers: consumer class, MediatR command wiring, DI registration, MassTransit test, and queue naming.

---

## When to Use
- An integration event already exists in `AK.BuildingBlocks/Messaging/IntegrationEvents/`
- Your service needs to react to that event
- Examples: AK.Payments starts consuming `OrderCancelledIntegrationEvent` to auto-refund; AK.Products consumes `OrderCancelledIntegrationEvent` to release reserved stock

## Prerequisite Reading
- [EVENTBUS.md](../../EVENTBUS.md) — fan-out topology, queue naming, dead-letter queues
- [new-integration-event.md](new-integration-event.md) — if the event doesn't exist yet, create it first

---

## Step 1 — Check the Event Contract

Find the event in `AK.BuildingBlocks/AK.BuildingBlocks/Messaging/IntegrationEvents/`. Understand every field — your consumer must not call back to the publisher's API to fetch additional data.

```bash
# Quick search
grep -rn "IntegrationEvent" AK.BuildingBlocks/AK.BuildingBlocks/Messaging/IntegrationEvents/
```

---

## Step 2 — Create the MediatR Command (if needed)

If the consumer triggers business logic, create a dedicated MediatR command in the Application layer. Keep the consumer thin — it should only unpack the event and delegate to MediatR.

```csharp
// AK.Payments/AK.Payments.Application/Commands/RefundOnCancellation/RefundOnCancellationCommand.cs
public record RefundOnCancellationCommand(
    Guid OrderId,
    string OrderNumber,
    string UserId) : IRequest;
```

Write the handler, validator, and unit tests following [new-endpoint.md](new-endpoint.md) Steps 3–5 (skip the endpoint registration step — consumers call MediatR directly).

---

## Step 3 — Create the Consumer Class

Place in `AK.<Service>/AK.<Service>.Infrastructure/Consumers/` or `AK.<Service>.Infrastructure/EventBus/`:

```csharp
// AK.Payments/AK.Payments.Infrastructure/Consumers/OrderCancelledConsumer.cs
public class OrderCancelledConsumer(
    IMediator mediator,
    ILogger<OrderCancelledConsumer> logger)
    : IConsumer<OrderCancelledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelledIntegrationEvent> context)
    {
        var evt = context.Message;

        logger.LogInformation(
            "Consumed OrderCancelledIntegrationEvent for order {OrderNumber}, user {UserId}",
            evt.OrderNumber, evt.UserId);

        await mediator.Send(new RefundOnCancellationCommand(
            Guid.Parse(evt.OrderId),
            evt.OrderNumber,
            evt.UserId));
    }
}
```

**Rules:**
- `IConsumer<TEvent>` from `MassTransit`
- Inject `IMediator` — do not call repositories or DbContext directly from the consumer
- Log at least the event's primary correlation ID (OrderId, PaymentId, etc.)
- Only throw from `Consume` if you want MassTransit to retry. Catch and handle expected failures (e.g. idempotency — order already refunded) inside the handler, not here.

---

## Step 4 — Register the Consumer

Open the service's Infrastructure `ServiceCollectionExtensions.cs` and add the consumer to the `AddRabbitMqMassTransit` configure callback:

```csharp
services.AddRabbitMqMassTransit(configuration, "payments", configure =>
{
    configure.AddConsumer<PaymentInitiatedAuditConsumer>();   // existing
    configure.AddConsumer<OrderCancelledConsumer>();           // ← new
});
```

The `"payments"` prefix creates a uniquely-named queue `payments-order-cancelled` bound to the `order-cancelled` RabbitMQ exchange. Multiple services can consume the same event independently (fan-out, not competing consumers).

---

## Step 5 — Verify the Queue Name

Queue name = `{servicePrefix}-{event-kebab-case}`.

| Service prefix | Event | Resulting queue |
|----------------|-------|-----------------|
| `payments` | `OrderCancelledIntegrationEvent` | `payments-order-cancelled` |
| `notification` | `OrderCancelledIntegrationEvent` | `notification-order-cancelled` |
| `products` | `OrderCancelledIntegrationEvent` | `products-order-cancelled` |

Confirm no other consumer in the same service is already consuming the same event — duplicate registration causes MassTransit to create two competing consumers on the same queue.

---

## Step 6 — Write the Integration Test

In `AK.IntegrationTests/` add a test for the new consumer using the MassTransit in-memory test harness:

```csharp
public class OrderCancelledPaymentsConsumerTests
{
    [Fact]
    public async Task OrderCancelledConsumer_WhenEventPublished_InitiatesRefund()
    {
        // Arrange
        var mockMediator = new Mock<IMediator>();
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderCancelledConsumer>())
            .AddSingleton(mockMediator.Object)
            .AddLogging()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var evt = new OrderCancelledIntegrationEvent(
            Guid.NewGuid().ToString(),
            "ORD-20260517-ABCD1234",
            Guid.NewGuid().ToString(),
            "test@example.com",
            "Test User",
            DateTimeOffset.UtcNow);

        // Act
        await harness.Bus.Publish(evt);

        // Assert
        var consumerHarness = harness.GetConsumerHarness<OrderCancelledConsumer>();
        (await consumerHarness.Consumed.Any<OrderCancelledIntegrationEvent>()).Should().BeTrue();
        mockMediator.Verify(
            m => m.Send(It.IsAny<RefundOnCancellationCommand>(), default),
            Times.Once);
    }
}
```

---

## Step 7 — Build and Test

```bash
dotnet build
dotnet test
```

---

## Step 8 — Update EVENTBUS.md

Add a row to the **Queues** table in `EVENTBUS.md`:

```markdown
| `payments-order-cancelled` | `OrderCancelledIntegrationEvent` | `OrderCancelledConsumer` in AK.Payments |
```

Also update the target service's `<SERVICE>_TECHNICAL_DESIGN.md` — add the event to "Events Consumed" table.

---

## Step 9 — Idempotency Consideration

RabbitMQ may redeliver messages (network hiccup, consumer restart). Ensure the handler is idempotent:
- Check if the refund already exists before creating a new one
- Use the event's `OrderId` or `PaymentId` as the idempotency key
- Return early (not throw) if already processed — throwing causes MassTransit to retry

---

## Checklist

- [ ] Event contract confirmed in BuildingBlocks (fields sufficient for your needs)
- [ ] MediatR command created for the business logic
- [ ] Consumer class created (`IConsumer<T>`, thin — delegates to MediatR)
- [ ] Consumer registered in `AddRabbitMqMassTransit` configure callback
- [ ] Queue name verified (no duplicate consumer for same event in same service)
- [ ] MassTransit in-memory integration test written and passing
- [ ] Handler is idempotent (safe to receive duplicate events)
- [ ] `EVENTBUS.md` Queues table updated
- [ ] Target service design doc "Events Consumed" table updated
