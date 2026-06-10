# Skill: Add a New Consumer

**Purpose:** Add a MassTransit consumer to an existing AntKart service to handle an already-defined integration event from BuildingBlocks. Covers: consumer class, MediatR command wiring, DI registration, MassTransit test, and receive-endpoint naming on Azure Service Bus.

---

## When to Use
- An integration event already exists in `AK.BuildingBlocks/Messaging/IntegrationEvents/`
- Your service needs to react to that event
- Examples: AK.Payments starts consuming `OrderCancelledIntegrationEvent` to auto-refund; AK.Products consumes `OrderCancelledIntegrationEvent` to release reserved stock

## Prerequisite Reading
- [EVENTBUS.md](../design/EVENTBUS.md) — fan-out topology (integration-events topic + subscriptions), receive-endpoint naming, dead-lettering, and IaC-owned topology
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

Open the service's Infrastructure `ServiceCollectionExtensions.cs` and add the consumer to the `AddAzureServiceBusMassTransit` configure callback:

```csharp
services.AddAzureServiceBusMassTransit(configuration, "payments", configure =>
{
    configure.AddConsumer<PaymentInitiatedAuditConsumer>();   // existing
    configure.AddConsumer<OrderCancelledConsumer>();           // ← new
});
```

The `"payments"` prefix gives the consumer a uniquely-named receive endpoint (`payments-order-cancelled`). The event is delivered through the service's **subscription** on the `integration-events` topic, so multiple services can consume the same event independently (fan-out, not competing consumers).

> **Topology is owned by infrastructure-as-code.** The application's identity holds only Service Bus Data Sender/Receiver — it does not create or alter entities at runtime. Adding a consumer to a service that **already has a subscription** needs no new entity. But if this is the service's **first** consumer (it has no subscription yet), or you are introducing a new command **queue**, that entity must be **added in infrastructure-as-code** — it is not provisioned by the app.

---

## Step 5 — Verify the Receive-Endpoint Name

Receive-endpoint name = `{servicePrefix}-{event-kebab-case}`.

| Service prefix | Event | Resulting endpoint |
|----------------|-------|--------------------|
| `payments` | `OrderCancelledIntegrationEvent` | `payments-order-cancelled` |
| `notification` | `OrderCancelledIntegrationEvent` | `notification-order-cancelled` |
| `products` | `OrderCancelledIntegrationEvent` | `products-order-cancelled` |

Confirm no other consumer in the same service is already consuming the same event — duplicate registration would route the same event to two consumers on the same subscription.

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

## Step 8 — Update docs/design/EVENTBUS.md

Update the **Events and their consumers** table in `docs/design/EVENTBUS.md` to add the service as a consumer of the event (and, if a new subscription/queue was required, note it as an infrastructure-as-code change).

Also update the target service's `<SERVICE>_TECHNICAL_DESIGN.md` — add the event to its "Events Consumed" table.

---

## Step 9 — Idempotency Consideration

Service Bus guarantees at-least-once delivery, so a message may be redelivered (network hiccup, consumer restart). Ensure the handler is idempotent:
- Check if the refund already exists before creating a new one
- Use the event's `OrderId` or `PaymentId` as the idempotency key
- Return early (not throw) if already processed — throwing causes MassTransit to retry

---

## Checklist

- [ ] Event contract confirmed in BuildingBlocks (fields sufficient for your needs)
- [ ] MediatR command created for the business logic
- [ ] Consumer class created (`IConsumer<T>`, thin — delegates to MediatR)
- [ ] Consumer registered in `AddAzureServiceBusMassTransit` configure callback
- [ ] Receive-endpoint name verified (no duplicate consumer for same event in same service)
- [ ] If a new subscription/queue was needed, it was added in infrastructure-as-code
- [ ] MassTransit in-memory integration test written and passing
- [ ] Handler is idempotent (safe to receive duplicate events)
- [ ] `docs/design/EVENTBUS.md` Events-and-their-consumers table updated
- [ ] Target service design doc "Events Consumed" table updated
