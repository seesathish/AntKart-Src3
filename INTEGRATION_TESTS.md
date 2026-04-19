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

## Manual E2E Testing (Docker Compose)

The automated unit tests above use an in-memory harness. The steps below let you walk the **full live async flow** — real RabbitMQ, real PostgreSQL, real MongoDB — so you can watch each stage happen in real time.

### Prerequisites

- Docker Desktop running
- All services built and started:

```bash
docker-compose up --build
```

Wait until these are healthy (check `docker-compose ps`):

| Container | Health |
|-----------|--------|
| `antcart-rabbitmq` | healthy |
| `antcart-elasticsearch` | healthy |
| `antcart-postgres` | up |
| `antcart-mongodb` | up |
| `antcart-keycloak` | up (may show unhealthy — still works) |
| `ak-order-api` | up |
| `ak-products-api` | up |
| `ak-gateway-api` | up |

---

### Step 1 — Get a JWT token

```bash
TOKEN=$(curl -s -X POST http://localhost:9090/gateway/identity/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"user1","password":"user123"}' \
  | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

echo "Token: ${TOKEN:0:60}..."
```

> Pre-seeded users: `user1/user123` (user role), `admin/admin123` (admin role).

---

### Step 2 — Browse the product catalogue

```bash
# Via API Gateway (recommended)
curl -s "http://localhost:9090/gateway/products?page=1&pageSize=3" \
  -H "Authorization: Bearer $TOKEN" | grep -o '"sku":"[^"]*"'

# Direct (skips gateway)
curl -s "http://localhost:8080/api/v1/products?page=1&pageSize=3" \
  | grep -o '"sku":"[^"]*"'
```

Pick a product and note its `id`, `sku`, and `price`. You can also check its current stock:

```bash
PRODUCT_ID="<paste id here>"
curl -s "http://localhost:8080/api/v1/products/$PRODUCT_ID" \
  | grep -o '"stockQuantity":[0-9]*'
```

---

### Step 3 — Place an order (triggers the SAGA)

```bash
# Set these from Step 2
PRODUCT_ID="<product id>"
SKU="<sku>"
PRICE=<price>

ORDER=$(curl -s -X POST http://localhost:9090/gateway/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"userId\": \"user1\",
    \"order\": {
      \"shippingAddress\": {
        \"fullName\": \"Jane Doe\",
        \"addressLine1\": \"42 Commerce St\",
        \"city\": \"Austin\",
        \"state\": \"TX\",
        \"postalCode\": \"73301\",
        \"country\": \"US\",
        \"phone\": \"+1-512-555-0199\"
      },
      \"items\": [{
        \"productId\": \"$PRODUCT_ID\",
        \"productName\": \"My Product\",
        \"sKU\": \"$SKU\",
        \"price\": $PRICE,
        \"quantity\": 1
      }]
    }
  }")

ORDER_ID=$(echo $ORDER | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "OrderId:  $ORDER_ID"
echo "Number:   $(echo $ORDER | grep -o '"orderNumber":"[^"]*"' | cut -d'"' -f4)"
echo "Status:   $(echo $ORDER | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4)"
```

Expected: `"status":"Pending"` — order saved, `OrderCreatedIntegrationEvent` published to outbox.

---

### Step 4 — Watch the SAGA complete

Poll the order status until it transitions out of Pending:

```bash
for i in 1 2 3 4 5; do
  STATUS=$(curl -s "http://localhost:9090/gateway/orders/$ORDER_ID" \
    -H "Authorization: Bearer $TOKEN" \
    | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4)
  echo "Check $i: $STATUS"
  [ "$STATUS" = "Confirmed" ] || [ "$STATUS" = "Cancelled" ] && break
done
```

Expected result within **1–2 seconds**:

```
Check 1: Confirmed   ← stock was available → SAGA happy path
```

or

```
Check 1: Cancelled   ← insufficient stock → SAGA sad path
```

---

### Step 5 — Verify stock was decremented

```bash
curl -s "http://localhost:8080/api/v1/products/$PRODUCT_ID" \
  | grep -o '"stockQuantity":[0-9]*'
```

Stock should be **exactly 1 less** than it was before the order. If stock was 0 to begin with the order would be `Cancelled`.

---

### Step 6 — Inspect RabbitMQ message flow

Open the management UI: **http://localhost:15672** (guest / guest)

Navigate to **Exchanges** and look for:

| Exchange | Expected after order | Expected after SAGA |
|----------|---------------------|---------------------|
| `AK.BuildingBlocks…:OrderCreatedIntegrationEvent` | message published | delivered |
| `AK.BuildingBlocks…:StockReservedIntegrationEvent` | — | message published |
| `AK.BuildingBlocks…:OrderConfirmedIntegrationEvent` | — | message published |

All queues should show **0 messages ready** (everything consumed).

---

### Step 7 — Verify logs in Kibana

Open Kibana: **http://localhost:5601**

1. Go to **Discover** (create a Data View `antkart-logs-*` on first visit, time field `@timestamp`)
2. Filter: `ServiceName: "AK.Order.API"`
3. You should see log entries for the order creation request
4. Filter: `ServiceName: "AK.Products.API"` to see the stock reservation log
5. Filter by `CorrelationId` to trace a single request across all services

---

### Step 8 — Sad path test (force a stock failure)

To exercise the cancellation path, order a quantity larger than available stock:

```bash
PRODUCT_RESP=$(curl -s "http://localhost:8080/api/v1/products?page=1&pageSize=1")
PRODUCT_ID=$(echo $PRODUCT_RESP | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
SKU=$(echo $PRODUCT_RESP | grep -o '"sku":"[^"]*"' | head -1 | cut -d'"' -f4)
PRICE=$(echo $PRODUCT_RESP | grep -o '"price":[0-9.]*' | head -1 | cut -d':' -f2)
STOCK=$(curl -s "http://localhost:8080/api/v1/products/$PRODUCT_ID" \
  | grep -o '"stockQuantity":[0-9]*' | cut -d':' -f2)

echo "Current stock: $STOCK — ordering $((STOCK + 99)) to force failure"

ORDER=$(curl -s -X POST http://localhost:9090/gateway/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"userId\": \"user1\",
    \"order\": {
      \"shippingAddress\": {
        \"fullName\": \"Jane Doe\",
        \"addressLine1\": \"42 Commerce St\",
        \"city\": \"Austin\",\"state\":\"TX\",
        \"postalCode\": \"73301\",\"country\":\"US\",
        \"phone\": \"+1-512-555-0199\"
      },
      \"items\": [{
        \"productId\": \"$PRODUCT_ID\",
        \"productName\": \"Test\",
        \"sKU\": \"$SKU\",
        \"price\": $PRICE,
        \"quantity\": $((STOCK + 99))
      }]
    }
  }")

ORDER_ID=$(echo $ORDER | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "OrderId: $ORDER_ID"
```

Then poll as in Step 4 — expected final status: **`Cancelled`**.

---

### Step 9 — Test rate limiting (Gateway)

Send more than 20 requests/second to trigger the gateway rate limiter on the products route:

```bash
for i in $(seq 1 25); do
  CODE=$(curl -s -o /dev/null -w "%{http_code}" \
    "http://localhost:9090/gateway/products?page=1&pageSize=1" \
    -H "Authorization: Bearer $TOKEN")
  echo "Request $i: HTTP $CODE"
done
```

After ~20 requests you should see `429 Too Many Requests`.

---

### Step 10 — Gateway health checks

All downstream services are reachable via the gateway:

```bash
curl -s -o /dev/null -w "products:  %{http_code}\n" http://localhost:9090/gateway/health/products
curl -s -o /dev/null -w "orders:    %{http_code}\n" http://localhost:9090/gateway/health/orders
curl -s -o /dev/null -w "cart:      %{http_code}\n" http://localhost:9090/gateway/health/cart
curl -s -o /dev/null -w "identity:  %{http_code}\n" http://localhost:9090/gateway/health/identity
```

All should return `200`.

---

## Architecture Notes

- Tests use `IAsyncLifetime` for harness start/stop lifecycle
- Each test class gets its own `ServiceProvider` and harness instance — tests are isolated
- `Task.Delay(300–500ms)` allows async message processing before asserting; MassTransit in-memory is fast
- The test project references Application layers only (no Infrastructure, no API) per the layer dependency rules
