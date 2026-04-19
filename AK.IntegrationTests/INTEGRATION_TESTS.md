# AntKart — Integration Tests Technical Design

## Overview

`AK.IntegrationTests` exercises the SAGA choreography, event bus flows, and payment event routing using **MassTransit's in-memory test harness** — no RabbitMQ, no database, no running host. All 28 tests run in ~12 seconds.

---

## Project Structure

```
AK.IntegrationTests/
├── AK.IntegrationTests.csproj
├── Common/
│   ├── TestHarnessFactory.cs            ← ServiceProvider builders (4 factory methods)
│   ├── IntegrationTestData.cs           ← Event factory helpers
│   └── PaymentInitiatedAuditConsumer.cs ← Test-only no-op consumer for audit assertions
├── Sagas/
│   ├── OrderSagaHappyPathTests.cs       ← 3 happy-path saga tests
│   └── OrderSagaSadPathTests.cs         ← 3 sad-path saga tests
├── EventBus/
│   ├── EventBusFlowTests.cs             ← 4 order event bus flow tests
│   └── PaymentEventBusFlowTests.cs      ← 7 payment event bus flow tests
└── Payments/
    ├── PaymentFlowHappyPathTests.cs     ← 5 payment happy-path tests
    └── PaymentFlowSadPathTests.cs       ← 6 payment sad-path tests
```

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| MassTransit | 8.3.6 | Test harness (built in) |
| xunit | 2.9.3 | Test runner |
| FluentAssertions | 7.0.0 | Assertions |
| Moq | 4.20.72 | Mocks (IUnitOfWork for payment consumers) |

---

## TestHarnessFactory

```csharp
// Saga-only harness — for state machine tests
TestHarnessFactory.CreateWithSaga();

// Order harness — saga + order consumers + cart consumer
TestHarnessFactory.CreateWithConsumers();

// Payment harness — saga + payment consumers (mocked IUnitOfWork)
TestHarnessFactory.CreateWithPaymentConsumers();

// Full harness — all of the above combined
TestHarnessFactory.CreateWithAllConsumers();
```

`CreateWithPaymentConsumers()` and `CreateWithAllConsumers()` register a Moq `IUnitOfWork` whose `GetByIdAsync` returns `null`. Payment consumers handle this with `if (order is null) return`, allowing bus routing to be verified without a real database.

---

## Test Coverage

### Order Saga — Happy Path (3 tests)

| Test | Verifies |
|------|---------|
| `OrderCreated_TransitionsTo_StockPending` | Saga created, `OrderCreatedIntegrationEvent` consumed |
| `StockReserved_TransitionsTo_Confirmed_AndPublishesOrderConfirmedEvent` | Saga publishes `OrderConfirmedIntegrationEvent` on stock success |
| `FullHappyPath_OrderCreated_StockReserved_OrderConfirmed` | End-to-end happy path; `OrderCancelledIntegrationEvent` not published |

### Order Saga — Sad Path (3 tests)

| Test | Verifies |
|------|---------|
| `StockReservationFailed_TransitionsTo_Cancelled_AndPublishesOrderCancelledEvent` | Saga publishes cancellation on stock failure |
| `StockReservationFailed_DoesNotPublishOrderConfirmedEvent` | Confirmed event never published in sad path |
| `FullSadPath_OrderCreated_StockFailed_OrderCancelled` | Cancellation reason propagated correctly |

### Order Event Bus Flow (4 tests)

| Test | Verifies |
|------|---------|
| `OrderCreatedEvent_IsConsumedBySaga` | Basic consumption |
| `StockReservedEvent_IsConsumedBySaga_AndPublishesConfirmed` | Stock success flow |
| `StockFailedEvent_IsConsumedBySaga_AndPublishesCancelled` | Stock failure flow |
| `MultipleOrders_EachSagaIsIsolated` | Two concurrent orders; saga instances don't interfere |

### Payment Event Bus Flow (7 tests)

| Test | Verifies |
|------|---------|
| `PaymentInitiatedEvent_IsConsumedByBus` | `PaymentInitiatedIntegrationEvent` routes and is consumed |
| `PaymentSucceededEvent_IsConsumedBySaga_AndNotCancelled` | Succeeded event consumed; no failure published |
| `PaymentFailedEvent_IsConsumedBySaga_AndNotSucceeded` | Failed event consumed; no success published |
| `FullOrderToPayment_HappyPath_AllEventsConsumed` | Full flow: order → stock → confirmed → payment success; no cancellations |
| `FullOrderToPayment_SadPath_StockFails_PaymentNeverInitiated` | Order cancelled on stock failure; payment never initiated |
| `FullOrderToPayment_SadPath_PaymentFails_AllEventsConsumed` | Full flow through to payment failure; no success published |
| `TwoConcurrentOrders_PaymentEventsIsolated` | Two simultaneous orders; payment events don't cross-contaminate |

### Payment — Happy Path (5 tests)

| Test | Verifies |
|------|---------|
| `PaymentInitiated_IsPublishedAndConsumed` | `PaymentInitiatedIntegrationEvent` consumed |
| `PaymentSucceeded_IsConsumedByPaymentSucceededConsumer` | Correct consumer handles the event by payment and order ID |
| `PaymentSucceeded_DoesNotPublishPaymentFailed` | Success path never emits a failure event |
| `FullE2E_OrderCreated_StockReserved_OrderConfirmed_PaymentSucceeded` | Full happy path; no cancellation in entire flow |
| `PaymentInitiated_CorrectAmountAndCurrency` | Amount and currency values survive the bus round-trip |

### Payment — Sad Path (6 tests)

| Test | Verifies |
|------|---------|
| `PaymentFailed_IsConsumedByPaymentFailedConsumer` | Correct consumer handles the event |
| `PaymentFailed_DoesNotPublishPaymentSucceeded` | Failure path never emits a success event |
| `PaymentFailed_PropagatesFailureReason` | Failure reason preserved through the bus |
| `FullE2E_OrderCreated_StockReserved_OrderConfirmed_PaymentFailed` | Full sad path; no success published |
| `FullE2E_OrderCreated_StockFailed_NeverReachesPayment` | Stock failure short-circuits before payment |
| `MultiplePayments_IsolatedFailures_DoNotCrossContaminate` | Two simultaneous failed payments don't mix |

---

## Test Harness API

```csharp
// Publish to bus
await _harness.Bus.Publish(evt);

// Assert consumed
(await _harness.Consumed.Any<TEvent>()).Should().BeTrue();

// Assert published (by a consumer, not by the test)
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

## Manual E2E Testing (Docker Compose)

The automated tests above use an in-memory harness. The steps below walk the **full live async flow** — real RabbitMQ, real PostgreSQL, real MongoDB, real Razorpay sandbox.

### Prerequisites

```bash
docker-compose up --build
```

Wait until healthy (check `docker-compose ps`):

| Container | Health |
|-----------|--------|
| `antcart-rabbitmq` | healthy |
| `antcart-elasticsearch` | healthy |
| `antcart-postgres` | up |
| `antcart-mongodb` | up |
| `antcart-keycloak` | up |
| `ak-order-api` | up |
| `ak-payments-api` | up |
| `ak-gateway-api` | up |

---

### Step 1 — Get a JWT token

```bash
TOKEN=$(curl -s -X POST http://localhost:9090/gateway/identity/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"user1","password":"user123"}' \
  | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
```

---

### Step 2 — Place an order (triggers the SAGA)

```bash
ORDER=$(curl -s -X POST http://localhost:9090/gateway/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "userId": "user1",
    "order": {
      "shippingAddress": {
        "fullName": "Jane Doe", "addressLine1": "42 Commerce St",
        "city": "Austin", "state": "TX",
        "postalCode": "73301", "country": "US", "phone": "+1-512-555-0199"
      },
      "items": [{"productId": "<id>", "productName": "T-Shirt",
                 "sKU": "MEN-SHIR-001", "price": 29.99, "quantity": 1}]
    }
  }')

ORDER_ID=$(echo $ORDER | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "OrderId: $ORDER_ID"
```

---

### Step 3 — Poll until SAGA confirms the order

```bash
for i in 1 2 3 4 5; do
  STATUS=$(curl -s "http://localhost:9090/gateway/orders/$ORDER_ID" \
    -H "Authorization: Bearer $TOKEN" \
    | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4)
  echo "Check $i: $STATUS"
  [ "$STATUS" = "Confirmed" ] || [ "$STATUS" = "Cancelled" ] && break
  sleep 1
done
```

---

### Step 4 — Initiate payment (Razorpay sandbox)

```bash
PAYMENT=$(curl -s -X POST http://localhost:9090/gateway/payments/initiate \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"orderId\": \"$ORDER_ID\", \"userId\": \"user1\",
       \"amount\": 29.99, \"currency\": \"INR\", \"method\": \"Card\"}")

echo $PAYMENT
```

Use the `razorpayOrderId` from the response in the Razorpay checkout / test payment flow.

---

### Step 5 — Verify payment (simulate webhook callback)

```bash
curl -s -X POST http://localhost:9090/gateway/payments/verify \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"orderId\": \"$ORDER_ID\",
    \"razorpayPaymentId\": \"pay_test_xxxxx\",
    \"razorpayOrderId\": \"order_xxxxx\",
    \"razorpaySignature\": \"<hmac_sig>\"
  }"
```

Order status should transition to `Paid`.

---

### Step 6 — Inspect RabbitMQ

Open **http://localhost:15672** (guest / guest) → Exchanges. Look for:

| Exchange | Published after |
|----------|----------------|
| `OrderCreatedIntegrationEvent` | POST /orders |
| `StockReservedIntegrationEvent` | Products service processes stock |
| `OrderConfirmedIntegrationEvent` | SAGA confirms order |
| `PaymentInitiatedIntegrationEvent` | POST /payments/initiate |
| `PaymentSucceededIntegrationEvent` | POST /payments/verify (success) |

---

### Step 7 — Gateway health checks

```bash
curl -s -o /dev/null -w "products:  %{http_code}\n" http://localhost:9090/gateway/health/products
curl -s -o /dev/null -w "orders:    %{http_code}\n" http://localhost:9090/gateway/health/orders
curl -s -o /dev/null -w "payments:  %{http_code}\n" http://localhost:9090/gateway/health/payments
curl -s -o /dev/null -w "cart:      %{http_code}\n" http://localhost:9090/gateway/health/cart
curl -s -o /dev/null -w "identity:  %{http_code}\n" http://localhost:9090/gateway/health/identity
```

All should return `200`.

---

## Architecture Notes

- Tests use `IAsyncLifetime` for harness start/stop lifecycle
- Each test class gets its own `ServiceProvider` and harness instance — tests are fully isolated
- `Task.Delay(300–500ms)` allows async message processing before asserting; MassTransit in-memory is fast
- The test project references Application layers only (no Infrastructure, no API) per layer dependency rules
- `PaymentInitiatedAuditConsumer` is a test-only no-op that simulates an audit/notification service, enabling `Consumed.Any<PaymentInitiatedIntegrationEvent>()` assertions
