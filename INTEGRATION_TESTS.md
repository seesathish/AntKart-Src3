# AntKart — Integration Tests Technical Design

## Overview

`AK.IntegrationTests` exercises the SAGA choreography and event bus flows using **MassTransit's in-memory test harness** — no RabbitMQ, no database, no running host. All 10 tests run in ~4 seconds.

---

## Project Structure

```
AK.IntegrationTests/
├── AK.IntegrationTests.csproj
├── Common/
│   ├── TestHarnessFactory.cs      ← ServiceProvider builders
│   └── IntegrationTestData.cs     ← Event factory helpers
├── Sagas/
│   ├── OrderSagaHappyPathTests.cs ← 3 happy-path tests
│   └── OrderSagaSadPathTests.cs   ← 3 sad-path tests
└── EventBus/
    └── EventBusFlowTests.cs       ← 4 event bus flow tests
```

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| MassTransit | 8.3.6 | Test harness (built in) |
| xunit | 2.9.3 | Test runner |
| FluentAssertions | 7.0.0 | Assertions |
| Moq | 4.20.72 | Mocks (where needed) |

---

## TestHarnessFactory

```csharp
// Saga-only harness — for state machine tests
TestHarnessFactory.CreateWithSaga();

// Full harness — saga + all consumers
TestHarnessFactory.CreateWithConsumers();
```

Both use `AddMassTransitTestHarness()` with an **in-memory saga repository**. No external dependencies.

---

## Test Coverage

### Happy Path (3 tests)

| Test | Verifies |
|------|---------|
| `OrderCreated_TransitionsTo_StockPending` | Saga created, `OrderCreatedIntegrationEvent` consumed |
| `StockReserved_TransitionsTo_Confirmed_AndPublishesOrderConfirmedEvent` | Saga publishes `OrderConfirmedIntegrationEvent` on stock success |
| `FullHappyPath_OrderCreated_StockReserved_OrderConfirmed` | End-to-end happy path; `OrderCancelledIntegrationEvent` not published |

### Sad Path (3 tests)

| Test | Verifies |
|------|---------|
| `StockReservationFailed_TransitionsTo_Cancelled_AndPublishesOrderCancelledEvent` | Saga publishes cancellation on stock failure |
| `StockReservationFailed_DoesNotPublishOrderConfirmedEvent` | Confirmed event never published in sad path |
| `FullSadPath_OrderCreated_StockFailed_OrderCancelled` | Cancellation reason propagated correctly |

### Event Bus Flow (4 tests)

| Test | Verifies |
|------|---------|
| `OrderCreatedEvent_IsConsumedBySaga` | Basic consumption |
| `StockReservedEvent_IsConsumedBySaga_AndPublishesConfirmed` | Stock success flow |
| `StockFailedEvent_IsConsumedBySaga_AndPublishesCancelled` | Stock failure flow |
| `MultipleOrders_EachSagaIsIsolated` | Two concurrent orders; saga instances don't interfere |

---

## Test Harness API

```csharp
// Publish to bus
await _harness.Bus.Publish(evt);

// Assert consumed
(await _harness.Consumed.Any<TEvent>()).Should().BeTrue();

// Assert published
(await _harness.Published.Any<TEvent>(
    m => m.Context.Message.OrderId == orderId)).Should().BeTrue();

// Assert saga exists
var sagaHarness = _provider.GetRequiredService<ISagaStateMachineTestHarness<OrderSaga, OrderSagaState>>();
sagaHarness.Sagas.Contains(orderId).Should().NotBeNull();
```

---

## Running Tests

```bash
# All integration tests
dotnet test AK.IntegrationTests/AK.IntegrationTests.csproj

# With verbose output
dotnet test AK.IntegrationTests/AK.IntegrationTests.csproj --logger "console;verbosity=detailed"

# All tests in solution
dotnet test
```

---

## Architecture Notes

- Tests use `IAsyncLifetime` for harness start/stop lifecycle
- Each test class gets its own `ServiceProvider` and harness instance — tests are isolated
- `Task.Delay(300–500ms)` allows async message processing before asserting; MassTransit in-memory is fast
- The test project references Application layers only (no Infrastructure, no API) per the layer dependency rules
