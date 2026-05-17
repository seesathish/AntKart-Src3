# AntKart — Event Bus Technical Design

## Overview

Async communication between microservices uses **MassTransit 8.3.6** with **RabbitMQ 3.13** as the message broker. The order flow implements a **SAGA choreography pattern** with an **EF Core Outbox** to guarantee at-least-once delivery and prevent dual-write problems.

---

## Event Flow

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
flowchart TD
    classDef service fill:#4A90D9,stroke:#2471A3,color:#fff
    classDef event fill:#3498DB,stroke:#2471A3,color:#fff
    classDef saga fill:#E67E22,stroke:#D35400,color:#fff,font-weight:bold
    classDef database fill:#27AE60,stroke:#1E8449,color:#fff
    classDef decision fill:#E74C3C,stroke:#C0392B,color:#fff

    Client([Client]):::service
    OrderAPI[AK.Order API]:::service
    Handler[CreateOrderCommandHandler]:::service
    MQ_OC{{RabbitMQ: order-created}}:::event
    Saga[OrderSaga]:::saga
    RSC[ReserveStockConsumer\nAK.Products]:::service
    MongoDB[(MongoDB)]:::database

    Client -->|POST /api/orders| OrderAPI
    OrderAPI -->|CreateOrderCommand| Handler
    Handler -->|save Order + Outbox| MQ_OC
    MQ_OC --> Saga
    MQ_OC --> RSC
    RSC -->|load & validate stock| MongoDB
    RSC -->|all stock OK| MQ_SR{{RabbitMQ: stock-reserved}}:::event
    RSC -->|stock insufficient| MQ_SRF{{RabbitMQ: stock-reservation-failed}}:::event

    MQ_SR --> Saga
    MQ_SRF --> Saga

    Saga -->|StockReserved| MQ_OConf{{RabbitMQ: order-confirmed}}:::event
    Saga -->|StockReservationFailed| MQ_OCan{{RabbitMQ: order-cancelled}}:::event

    MQ_OConf --> OCC[OrderConfirmedConsumer\nAK.Order]:::service
    MQ_OConf --> CCC[ClearCartOnOrderConfirmedConsumer\nAK.ShoppingCart]:::service
    MQ_OCan --> OCan[OrderCancelledConsumer\nAK.Order]:::service

    OCC -->|Status = Confirmed| OrderDB[(PostgreSQL\nAKOrdersDb)]:::database
    CCC -->|DeleteCart| Redis[(Redis)]:::database
    CCC -->|CartClearedIntegrationEvent| MQ_CC{{RabbitMQ: cart-cleared}}:::event
    OCan -->|Status = Cancelled| OrderDB
```

---

## Integration Events (AK.BuildingBlocks)

| Event | Publisher | Subscribers |
|-------|-----------|-------------|
| `OrderCreatedIntegrationEvent` | AK.Order (handler) | AK.Order (OrderSaga) |
| `StockReservedIntegrationEvent` | AK.Products | AK.Order (OrderSaga) |
| `StockReservationFailedIntegrationEvent` | AK.Products | AK.Order (OrderSaga) |
| `OrderConfirmedIntegrationEvent` | AK.Order (OrderSaga) | AK.Order (consumer), AK.ShoppingCart (consumer) |
| `OrderCancelledIntegrationEvent` | AK.Order (OrderSaga) | AK.Order (consumer) |
| `CartClearedIntegrationEvent` | AK.ShoppingCart | — |
| `PaymentInitiatedIntegrationEvent` | AK.Payments | — |
| `PaymentSucceededIntegrationEvent` | AK.Payments | AK.Order (updates status → Paid) |
| `PaymentFailedIntegrationEvent` | AK.Payments | AK.Order (updates status → PaymentFailed) |

All events implement `IIntegrationEvent` and are `sealed record` types in `AK.BuildingBlocks/Messaging/IntegrationEvents/`.

**Payment event payloads:**

| Event | Fields |
|-------|--------|
| `PaymentInitiatedIntegrationEvent` | `paymentId`, `orderId`, `userId`, `amount`, `currency`, `razorpayOrderId` |
| `PaymentSucceededIntegrationEvent` | `paymentId`, `orderId`, `userId`, `razorpayPaymentId` |
| `PaymentFailedIntegrationEvent` | `paymentId`, `orderId`, `userId`, `reason` |

---

## SAGA State Machine

Location: `AK.Order/AK.Order.Application/Sagas/OrderSaga.cs`

> The saga runs entirely within the **AK.Order** service, persisted to PostgreSQL via EF Core.

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
stateDiagram-v2
    classDef sagaState fill:#E67E22,stroke:#D35400,color:#fff,font-weight:bold

    [*] --> StockPending : OrderCreated
    StockPending --> Confirmed : StockReserved\n→ publishes OrderConfirmed
    StockPending --> Cancelled : StockReservationFailed\n→ publishes OrderCancelled
    Confirmed --> [*] : saga complete
    Cancelled --> [*] : saga complete
```

**Correlation:** `OrderCreatedIntegrationEvent.OrderId` → `CorrelationId`

**State persistence:** PostgreSQL via EF Core (`order_saga_states` table), optimistic concurrency with `Version` column.

---

## Payment Event Flow

```mermaid
%%{init: {'theme': 'base', 'themeVariables': {'lineColor': '#888888', 'edgeLabelBackground': '#00000000'}}}%%
sequenceDiagram
    participant C as Client
    participant P as AK.Payments
    participant R as Razorpay
    participant MQ as RabbitMQ
    participant O as AK.Order

    C->>P: POST /api/payments/initiate
    P->>R: CreateOrder (amount, currency)
    R-->>P: razorpayOrderId
    P->>MQ: PaymentInitiatedIntegrationEvent\n(paymentId, orderId, userId, amount, currency, razorpayOrderId)
    P-->>C: 200 OK (razorpayOrderId, keyId)

    Note over C,R: Client completes payment on Razorpay checkout UI

    C->>P: POST /api/payments/verify\n(razorpayPaymentId, razorpayOrderId, razorpaySignature)
    P->>P: Verify HMAC-SHA256 signature

    alt Signature valid (happy path)
        P->>MQ: PaymentSucceededIntegrationEvent\n(paymentId, orderId, userId, razorpayPaymentId)
        MQ->>O: PaymentSucceededIntegrationEvent
        O->>O: Update Order.Status = Paid
        P-->>C: 200 OK (payment verified)
    else Signature mismatch (sad path)
        P->>MQ: PaymentFailedIntegrationEvent\n(paymentId, orderId, userId, reason)
        MQ->>O: PaymentFailedIntegrationEvent
        O->>O: Update Order.Status = PaymentFailed
        P-->>C: 400 Bad Request (signature invalid)
    end
```

---

## EF Core Outbox

The `OrderDbContext` includes MassTransit outbox entities:

```csharp
modelBuilder.AddInboxStateEntity();
modelBuilder.AddOutboxMessageEntity();
modelBuilder.AddOutboxStateEntity();
```

`CreateOrderCommandHandler` calls `IPublishEndpoint.Publish()` within the same EF Core transaction. MassTransit intercepts the call, stores the event in `outbox_message`, and delivers it after the DB commit — guaranteeing the order is never saved without the event being delivered.

---

## RabbitMQ Configuration

```json
"RabbitMq": {
  "Host": "rabbitmq",
  "VirtualHost": "/",
  "Username": "guest",
  "Password": "guest"
}
```

Exchange and queue names are auto-formatted by MassTransit using kebab-case convention (e.g., `order-created-integration-event`).

**Global retry policy** (configured in `MassTransitExtensions`):
- 3 retries with exponential back-off: 1s, 3s, 9s

---

## RabbitMQ Management Portal

### Access
| URL | `http://localhost:15672` |
|-----|--------------------------|
| Username | `guest` |
| Password | `guest` |

---

### What to look at

#### Exchanges
**RabbitMQ → Exchanges tab**

Every integration event gets its own exchange (named after the event class in kebab-case). Useful ones to inspect:

| Exchange | Published by |
|----------|-------------|
| `order-created-integration-event` | AK.Order |
| `stock-reserved-integration-event` | AK.Products |
| `stock-reservation-failed-integration-event` | AK.Products |
| `order-confirmed-integration-event` | AK.Order (SAGA) |
| `order-cancelled-integration-event` | AK.Order (SAGA) |
| `payment-initiated-integration-event` | AK.Payments |
| `payment-succeeded-integration-event` | AK.Payments |
| `payment-failed-integration-event` | AK.Payments |
| `user-registered-integration-event` | AK.UserIdentity |

Click an exchange → **Bindings** tab to see which queues are bound to it (fan-out delivery).

#### Queues
**RabbitMQ → Queues tab**

Each consumer gets its own uniquely-named queue (prefixed by service name):

| Queue | Consumer service |
|-------|----------------|
| `order-order-confirmed` | AK.Order — OrderConfirmedConsumer |
| `order-order-cancelled` | AK.Order — OrderCancelledConsumer |
| `order-payment-succeeded` | AK.Order — PaymentSucceededConsumer |
| `order-payment-failed` | AK.Order — PaymentFailedConsumer |
| `notification-user-registered` | AK.Notification |
| `notification-order-created` | AK.Notification |
| `notification-payment-succeeded` | AK.Notification |
| `notification-payment-failed` | AK.Notification |
| `products-reserve-stock` | AK.Products — ReserveStockConsumer |
| `cart-order-confirmed` | AK.ShoppingCart — ClearCartOnOrderConfirmedConsumer |

Click a queue to see:
- **Ready** — messages waiting to be consumed
- **Unacked** — messages being processed by a consumer
- **Total** — throughput counter
- **Messages** tab — browse actual message payloads (useful for debugging stuck messages)

#### Monitoring a live event flow
1. Go to **Queues** — note all queues show 0 Ready
2. Trigger an action (e.g. create an order via Postman)
3. Refresh Queues — messages briefly appear as Unacked then clear (fast consumers)
4. If a queue shows **Ready > 0** for more than a few seconds, the consumer is down or erroring — check that service's logs

#### Dead-letter queues
Failed messages after all retries (3 × exponential backoff) land in dead-letter queues named `<queue>_skipped` or `<queue>_error`. These appear in the Queues list if any message has been dead-lettered. Click the queue → **Get messages** to inspect the failed payload and reason.

---

### Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Queue has Ready messages accumulating | Consumer service is down | Check `docker logs antkart-<service>` |
| Exchange missing | Service never started (no consumers registered) | `docker-compose up` the relevant service |
| Messages appearing in `_error` queue | Consumer threw unhandled exception | Inspect message payload; check service logs |
| No exchanges visible at all | RabbitMQ just restarted; services not yet connected | Wait ~30s for services to reconnect; check service healthcheck |
| `guest` login rejected | RabbitMQ default user disabled | Check `RABBITMQ_DEFAULT_USER/PASS` env vars in docker-compose.yml |

---

## ReserveStockConsumer (AK.Products)

Location: `AK.Products/AK.Products.Application/Consumers/ReserveStockConsumer.cs`

1. Loads each product by ProductId from MongoDB
2. Validates all items have sufficient stock before applying any decrement (all-or-nothing)
3. On success: calls `Product.DecrementStock()` for each item, saves, publishes `StockReservedIntegrationEvent`
4. On failure: publishes `StockReservationFailedIntegrationEvent` with reason

> **Note:** MongoDB does not support multi-document transactions without a replica set. The consumer uses optimistic all-or-nothing validation before applying changes. `ConcurrentMessageLimit = 1` prevents race conditions in the test harness. For production at high scale, consider a MongoDB replica set.

---

## ClearCartOnOrderConfirmedConsumer (AK.ShoppingCart)

Location: `AK.ShoppingCart/AK.ShoppingCart.Application/Consumers/ClearCartOnOrderConfirmedConsumer.cs`

- Consumes `OrderConfirmedIntegrationEvent`
- Reads `UserId` from the event
- Calls `IUnitOfWork.Carts.DeleteAsync(userId)` if the cart exists
- Publishes `CartClearedIntegrationEvent`

---

## Order Consumers (AK.Order)

| Consumer | Event | Action |
|----------|-------|--------|
| `OrderConfirmedConsumer` | `OrderConfirmedIntegrationEvent` | Updates `Order.Status = Confirmed` |
| `OrderCancelledConsumer` | `OrderCancelledIntegrationEvent` | Updates `Order.Status = Cancelled` |
| `PaymentSucceededConsumer` | `PaymentSucceededIntegrationEvent` | Updates `Order.Status = Paid` |
| `PaymentFailedConsumer` | `PaymentFailedIntegrationEvent` | Updates `Order.Status = PaymentFailed` |

These keep the Order aggregate's status in sync after the SAGA finalises or payment completes.

---

## MassTransit Registration

Each service registers via `AddRabbitMqMassTransit()` (BuildingBlocks helper):

```csharp
// AK.Order
services.AddRabbitMqMassTransit(configuration, cfg =>
{
    cfg.AddSagaStateMachine<OrderSaga, OrderSagaState>()
       .EntityFrameworkRepository(r =>
       {
           r.ConcurrencyMode = ConcurrencyMode.Optimistic;
           r.ExistingDbContext<OrderDbContext>();
           r.UsePostgres();
       });
    cfg.AddEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    cfg.AddConsumer<OrderConfirmedConsumer>();
    cfg.AddConsumer<OrderCancelledConsumer>();
    cfg.AddConsumer<PaymentSucceededConsumer>();
    cfg.AddConsumer<PaymentFailedConsumer>();
});

// AK.Products
services.AddRabbitMqMassTransit(configuration, cfg =>
{
    cfg.AddConsumer<ReserveStockConsumer>();
});

// AK.ShoppingCart
services.AddRabbitMqMassTransit(configuration, cfg =>
{
    cfg.AddConsumer<ClearCartOnOrderConfirmedConsumer>();
});

// AK.Payments
services.AddRabbitMqMassTransit(configuration, cfg =>
{
    // publishes PaymentInitiatedIntegrationEvent,
    //           PaymentSucceededIntegrationEvent,
    //           PaymentFailedIntegrationEvent
});
```

---

## EF Core Migration

The `AddSagaAndOutbox` migration creates:

- `order_saga_states` — saga state table
- `InboxState` — MassTransit inbox deduplication
- `OutboxMessage` — outbox event store
- `OutboxState` — outbox delivery tracking

Run: `dotnet ef database update` from `AK.Order.Infrastructure` startup.
