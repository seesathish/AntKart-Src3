# AntKart — Claude Code Instructions

This file guides Claude Code on conventions, architecture, and rules for the AntKart microservices platform. Read this fully before starting any task.

---

## Platform Overview

AntKart is a cloud-native e-commerce platform built as independently deployable .NET 9 microservices. All services live in a single Git repository (`AntKart.sln`) organised as one top-level folder per microservice.

---

## Repository Layout

```
AntKart/
├── AK.Products/          REST Minimal API — product catalogue (Cosmos DB, MongoDB API)
├── AK.Discount/          gRPC service — discount coupons (SQLite)
├── AK.ShoppingCart/      REST Minimal API — shopping cart (Redis)
├── AK.Order/             REST Minimal API — order management (PostgreSQL + SAGA)
├── AK.Gateway/           API Gateway — Ocelot single entry point
├── AK.Payments/          REST Minimal API — payment processing (PostgreSQL + Razorpay)
├── AK.Notification/      Serverless notifications (single service folder — no microservice host)
│   ├── AK.Notification.Core/             Reusable notification library (channels, templates, ACS Email, EF history)
│   ├── AK.Notification.Functions/        .NET 9 isolated Azure Functions app — Event Grid-triggered, dispatches through AK.Notification.Core
│   └── AK.Notification.Tests/            Single test project (Core/ + Functions/ subfolders) covering both projects
├── AK.BuildingBlocks/    Shared cross-cutting library (no business logic)
├── AK.IntegrationTests/  SAGA + event bus tests (no API/Grpc dependency)
├── AK.Tools/             Developer tools
│   ├── AK.Tools.ProductsSeedGenerator/  Console tool — deterministically generates AK.Seed-Data/products.csv (3,000 products)
│   ├── AK.Tools.ProductsSeedLoader/      Console tool — idempotent, secret-less upsert of products.csv into Cosmos (keyed on SKU-derived id)
│   └── AK.Tools.ProductsSeedLoader.Tests/  Unit tests for the loader (CSV parsing + deterministic id, mocked sink)
├── AK.Seed-Data/         Committed product seed dataset (products.csv + README)
├── AntKart.sln
├── AntKart.postman_collection.json
├── KNOWN-ISSUES.md       Tracker for known technical debt & deferred fixes (KI-NNN ids)
├── docs/
│   ├── adr/              Architecture Decision Records
│   ├── architecture/     C4 diagram images + Structurizr workspace
│   ├── design/           Cross-cutting design docs (EVENTBUS, RESILIENCE, OBSERVABILITY)
│   ├── skills/           Step-by-step development & maintenance guides
│   └── test/             Manual test & security test guides (DevTestGuide, SECURITY_TESTS)
├── nuget.config
└── CLAUDE.md             ← this file
```

Each microservice folder contains **all** its layers and test project:

```
AK.<Service>/
  AK.<Service>.Domain/
  AK.<Service>.Application/
  AK.<Service>.Infrastructure/
  AK.<Service>.API  or  AK.<Service>.Grpc/
  AK.<Service>.Tests/
  <SERVICE>_TECHNICAL_DESIGN.md
```

---

## Completed Services

### ✅ AK.Products  (REST Minimal API)
- **Transport:** HTTP REST, port 5077 (dev) / 8080 (Docker)
- **Database:** Azure Cosmos DB (MongoDB API) — `antkart-products` / `products` collection. Sharded on `{ "_id": "hashed" }` (single-field, hashed shard key on the product id). Connection string is a secret read from Key Vault (config key/secret `ProductsCosmosConnectionString`); `appsettings` holds only the non-secret database/collection names. Local fallback: `mongodb://localhost:27017` when no vaulted secret. Wire-compatible, so `MongoDB.Driver` is unchanged.
- **Architecture:** DDD + Clean Architecture — `MongoDB.Driver` confined to Infrastructure only; Domain has zero infrastructure dependencies
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Specification, Unit of Work
- **Category design:** Data-driven — `CategoryName` (top-level: Men/Women/Kids/Sports/etc.) + `SubCategoryName` (specific: Shirts/Dresses/etc.) are plain strings; no hardcoded enum. Adding a new category is a data change only.
- **ID format:** `Guid.NewGuid().ToString("N")` — 32-char hex string, pure .NET, stored as BSON string
- **MongoDB mapping:** `ProductClassMap` (Infrastructure) registers `BsonClassMap<StringEntity>` and `BsonClassMap<Product>` — handles `SetIgnoreExtraElements` and unmaps `DomainEvents`; called before `MongoDbContext` is registered
- **Seed data:** 300 products driven by `CategoryDefinition` record array — 10 sub-categories × 10 products per top-level category. Currently seeded: Men, Women, Kids.
- **SKU format:** `{CAT_ABBREV}-{SUBCAT_ABBREV}-{001..NNN}` e.g. `MEN-SHIR-001`, `WOM-DRES-001`
- **New endpoint:** `GET /api/v1/products/categories` — returns distinct top-level category names from DB
- **Removed endpoints:** `/men`, `/women`, `/kids` — replaced by `?category=Men`, `?category=Women`, `?category=Kids`
- **Cosmos resilience:** `ProductRepository` runs every Cosmos call through the `"cosmos"` Polly v8 pipeline (`AddDataStoreResiliencePipeline`); `CosmosResilience` (Infrastructure) supplies the transient-fault rules and **honours the 429 `RetryAfterMs`** (falls back to exponential backoff + jitter). Retry lives at the data-access call site; BuildingBlocks carries no MongoDB dependency
- **Seed dataset & loader:** `AK.Seed-Data/products.csv` is a committed, deterministic dataset of **3,000 products** (1,000 each Men/Women/Kids) produced by `AK.Tools.ProductsSeedGenerator`. `AK.Tools.ProductsSeedLoader` upserts it into Cosmos **idempotently** — the document `_id` is derived from the SKU (MD5 → 32-hex), so the upsert is a single-partition point write on the hashed `_id` and re-running never duplicates. `Product.CreateForSeed(id, …)` assigns that deterministic id; the loader reuses `MongoDbContext`/`ProductClassMap` (no duplicated Mongo code) and reads the Cosmos connection string from Key Vault (secret-less)
- **Deep health checks:** `MongoDbHealthCheck` (real Cosmos `{ ping: 1 }`) + `AddKeyVaultDeepCheck()` are tagged `ak:deep` and surface only on `/health/deps` — never on liveness/readiness
- **Discount gRPC (optional dependency):** the `discount-grpc` client is tuned **fail-fast** (2s timeout, `AddOptionalDependencyResilience` — no retry + quick circuit-break) so a down AK.Discount never slows product queries; `DiscountGrpcClient` never throws (returns null), logs **one concise warning per request** (no stack trace) on unavailability, and the catalogue always renders without a discount price
- **Tests:** 218 passing (domain, commands, queries, validators, specifications, DTO mapping, GetProductCategories handler, ReserveStockConsumer, Cosmos resilience retry/RetryAfter, health-check registration, Discount gRPC graceful-degradation)
- **Swagger:** `http://localhost:5077/swagger` (Development only)
- **Design doc:** [AK.Products/PRODUCTS_TECHNICAL_DESIGN.md](AK.Products/PRODUCTS_TECHNICAL_DESIGN.md)

### ✅ AK.Discount  (gRPC)
- **Transport:** gRPC, port 5001 (dev) / 8081 (Docker)
- **Database:** SQLite (`discount.db`) via EF Core 9, code-first migrations
- **Architecture:** Clean Architecture (lighter — no DDD events)
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Repository
- **Proto:** `AK.Discount/AK.Discount.Grpc/Protos/discount.proto`
- **RPCs:** GetDiscount, CreateDiscount, UpdateDiscount, DeleteDiscount, GetAllDiscounts
- **Seed data:** 300 coupons matching AK.Products SKUs (one per product)
- **Tests:** 53 passing (domain, commands, queries, validators, DTO mapping)
- **Design doc:** [AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md](AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md)

### ✅ AK.ShoppingCart  (REST Minimal API)
- **Transport:** HTTP REST, port 5079 (dev) / 8082 (Docker)
- **Database:** Redis — key pattern `AKCart:cart:{userId}`, 30-day TTL
- **Architecture:** DDD + Clean Architecture
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Repository, Unit of Work
- **Operations:** Add to cart, remove item, update quantity, clear cart, get cart
- **Cart behaviour:** Adding existing product increments quantity; quantity=0 removes item
- **Serialisation:** `System.Text.Json` snapshot pattern (domain → CartSnapshot DTO → Redis)
- **Tests:** 88 passing (domain, commands, queries, validators, behaviors, infrastructure)
- **Swagger:** `http://localhost:5079/swagger` (Development only)
- **Design doc:** [AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md)

### ✅ AK.Order  (REST Minimal API)
- **Transport:** HTTP REST, port 5080 (dev) / 8083 (Docker)
- **Database:** PostgreSQL — `AKOrdersDb` via EF Core 9 + Npgsql, code-first migrations
- **Architecture:** Vertical Slice Clean Architecture (features organised by slice in Application layer)
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Specification, Repository, Unit of Work
- **Operations:** Create order, get order by ID, list orders (paged, filtered), list by user, update status, cancel
- **Order number format:** `ORD-{yyyyMMdd}-{8-char-GUID-uppercase}` e.g. `ORD-20260418-A1B2C3D4`
- **Domain events:** `OrderCreatedEvent`, `OrderStatusChangedEvent`, `OrderCancelledEvent`
- **Order status state machine:** `_allowedTransitions` dictionary enforces valid transitions — Pending→Confirmed|Cancelled|PaymentFailed, Confirmed→Processing|Shipped|Cancelled, Processing→Shipped|Cancelled, Shipped→Delivered, Delivered/Cancelled are terminal (no further transitions). `UpdateStatus()` throws `InvalidOperationException` for invalid transitions.
- **Result\<T\> pattern:** `CancelOrderCommandHandler` and `UpdateOrderStatusCommandHandler` return `Result<T>` (BuildingBlocks) instead of throwing for expected failures (not found, invalid transition, already cancelled, delivered). Endpoints map `Result.IsSuccess` → 204/200, `Result.Failure` → 409 with error message. `CreateOrderCommandHandler` deliberately uses exceptions — the CQRS article compares both approaches.
- **API versioning:** `AddStandardApiVersioning()` registered — v1.0 default, URL segment or `api-version` header. Other services adopt by calling the same single method.
- **Event Grid notification side-effects (commit-then-notify):** publishes the shared `AK.BuildingBlocks.Messaging.Notifications` contracts as fire-and-forget side-effects via `IEventGridSideEffectPublisher.TryPublishAsync` (never-throws), **strictly after the durable commit** — `OrderCreatedNotification` (CreateOrderCommandHandler), `OrderConfirmedNotification` (OrderConfirmedConsumer, now using the shared contract — no ad-hoc shape), `OrderCancelledNotification` (OrderCancelledConsumer — the single sink for both the customer-cancel and saga-cancel paths, so one notification per cancellation). A publish failure can never roll back or fail the business operation. Non-secret `EventGrid:TopicEndpoint` in `appsettings`
- **Tests:** 121 passing (domain, features, validators, behaviors, infrastructure with EF InMemory, OrderConfirmed/OrderCancelled consumer side-effects + decoupling, OrderCreated publish + publish-failure tolerance)
- **Swagger:** `http://localhost:5080/swagger` (Development only)
- **Design doc:** [AK.Order/ORDER_TECHNICAL_DESIGN.md](AK.Order/ORDER_TECHNICAL_DESIGN.md)

### ⛔ AK.UserIdentity — retired (ADR-021)
The dedicated identity microservice was removed in the Entra migration. Microsoft Entra ID issues access tokens directly to clients via standard OAuth/OIDC flows; each service validates them via `AddEntraAuthentication` (BuildingBlocks). User and app-role administration is an operational concern handled in Entra / Microsoft Graph — not an application service. See [ADR-021](docs/adr/ADR-021-retire-identity-service-for-entra.md).

### ✅ AK.Gateway  (API Gateway)
- **Transport:** HTTP, port 8000 (Docker) / 8000 (dev)
- **Technology:** Ocelot 23.4.2
- **Features:** Routing to all 4 REST services, JWT passthrough auth, rate limiting (10-30 RPS per route), QoS circuit breaker
- **Config:** `ocelot.json` (Docker), `ocelot.Development.json` (dev overrides)
- **Design doc:** [AK.Gateway/API_GATEWAY.md](AK.Gateway/API_GATEWAY.md)

### ✅ AK.BuildingBlocks  (Shared Library)
- `Common/PagedResult<T>`, `Result<T>`
- `Exceptions/NotFoundException`, `ValidationException`
- `Logging/SerilogExtensions` — Serilog with console + rolling file (the console stream is collected by Application Insights / Log Analytics in the cloud; no Elasticsearch/Kibana sink)
- `HealthChecks/HealthCheckExtensions` + `HealthCheckTags` — three probe surfaces wired into every service: `/health/live` (shallow `self`, no external calls — liveness, avoids restart storms), `/health/ready` (tolerant — Degraded ⇒ 200, avoids fleet blackout), `/health/deps` (all checks incl. deep + MassTransit bus, detailed JSON — diagnostics, not a probe), plus shallow `/health` alias. Tags are namespaced (`ak:live`/`ak:ready`/`ak:deep`) so a third-party check (e.g. MassTransit's `"ready"`-tagged bus check) cannot leak onto our readiness probe. `KeyVaultHealthCheck` + `AddKeyVaultDeepCheck()` is a reusable deep check (lists secret metadata only)
- `Middleware/CorrelationIdMiddleware` — `X-Correlation-Id` header
- `Authentication/AuthenticationExtensions` — `AddEntraAuthentication()` + `UseEntraAuth()` shared JWT auth wiring; validates the token against Microsoft Entra ID (issuer = tenant v2 issuer; **audience = either valid form** via `ValidAudiences` — the App ID URI `api://antkart-api-dev` OR the client-id GUID, so a token validates whether the caller is a separate client app or the API requesting a token for itself; lifetime; signature via Entra OIDC keys) and reads authorization from the **flat `roles` claim** (`RoleClaimType = "roles"`, `MapInboundClaims = false`)
- `Authentication/EntraSettings` — typed, non-secret config record (`Instance`, `TenantId`, `Audience` = App ID URI, `ClientId` = app's client-id GUID) bound from the `Entra` section; `ResolveValidAudiences()` returns both audience forms (empties filtered); derives authority/issuer as `{Instance}/{TenantId}/v2.0`
- `Authentication/HttpContextExtensions` — `GetUserId()` extracts `sub` from JWT; `GetUserEmail()` reads `email`/`ClaimTypes.Email`; `GetUserDisplayName()` reads `name`/`given_name`+`family_name`/`preferred_username`
- `DDD/IDomainEvent` — shared marker interface implemented by all domain event records (Products, Order, Payments)
- `DDD/IAggregateRoot` — shared marker interface identifying aggregate root entities
- `DDD/Entity` — shared abstract base for Guid-keyed entities (Order, Payments, Notification): `Guid Id`, `DateTimeOffset CreatedAt`, `DateTimeOffset? UpdatedAt`, `AddDomainEvent()`, `ClearDomainEvents()`, `SetUpdatedAt()`
- `DDD/StringEntity` — same as Entity but with `string Id = Guid.NewGuid().ToString("N")` for MongoDB entities (Products); replaces per-service `BaseEntity`/`Entity` duplicates
- `DDD/ValueObject` — abstract base for value objects; subclass and implement `GetEqualityComponents()` — `Equals()`, `GetHashCode()`, `==`, `!=` are derived automatically via `SequenceEqual`; used by `ShippingAddress` in AK.Order (deliberately contrasts with `Money` in AK.Products which uses a C# `record` — both approaches valid)
- `Messaging/IIntegrationEvent` — base interface for all integration events
- `Messaging/IntegrationEvents/` — `OrderCreatedIntegrationEvent` (enriched: CustomerEmail, CustomerName, OrderNumber), `OrderConfirmedIntegrationEvent` (enriched), `OrderCancelledIntegrationEvent` (enriched + UserId), `PaymentSucceededIntegrationEvent` (enriched), `PaymentFailedIntegrationEvent` (enriched), `StockReservedIntegrationEvent`, `StockReservationFailedIntegrationEvent`, `PaymentInitiatedIntegrationEvent` (consumed by `PaymentInitiatedAuditConsumer` in integration tests)
- `Behaviors/ValidationBehavior<TRequest, TResponse>` — shared MediatR pipeline behavior; every service wires this from BuildingBlocks; replaces per-service copies
- `Middleware/ExceptionHandlerMiddleware` — shared exception→HTTP mapper used by ShoppingCart, Order, Payments, Notification, Products
- `Messaging/MassTransitExtensions` — `AddAzureServiceBusMassTransit(config, servicePrefix, configure)` helper; connects to the Service Bus namespace (`ServiceBus:FullyQualifiedNamespace`, non-secret) with `DefaultAzureCredential` (Entra, no connection string). Each service passes a unique prefix ("order", "notification", "payments", "cart", "products") so consumers get uniquely-named receive endpoints. **Topology is owned by infrastructure-as-code** — the identity holds Service Bus Data Sender/Receiver (never Manage), so MassTransit uses the provisioned entities and does not create/alter topology at runtime
- `Email/IEmailSender` + `Email/AcsEmailSender` — reusable email sender over the `Azure.Communication.Email` SDK, used by the notification paths (Function + AK.Notification). `AddAcsEmailSender(configuration)` reads the non-secret `Acs` section and selects auth **Entra-first**: default is `EmailClient(endpoint, DefaultAzureCredential)` (managed identity, no secret); if `Acs:ConnectionString` is present (Key-Vault-sourced, never committed) it uses the connection-string client; if neither is configured it is a **safe no-op**. Sender display name applied as RFC 5322 `"AntKart <DoNotReply@…>"`. The managed identity needs the **Contributor** role on the ACS resource (no granular email-send role exists yet)
- `Messaging/Notifications/` — the shared customer-notification event contracts: `NotificationEventTypes` (Event Grid `eventType` string constants, e.g. `AntKart.Order.Confirmed`) + five payload records (`OrderCreatedNotification`, `OrderConfirmedNotification`, `OrderCancelledNotification`, `PaymentSucceededNotification`, `PaymentFailedNotification`). ONE definition referenced by both the notification Functions (consume) and Order/Payments (publish, next step)
- `Messaging/EventGrid/IEventGridSideEffectPublisher` + `EventGridSideEffectPublisher` — fire-and-forget Event Grid publisher for lightweight side-effects (notification), deliberately separate from the durable Service Bus saga. `TryPublishAsync(eventType, subject, data)` **never throws** — any failure is swallowed + logged, returns `false`, so a side-effect failure cannot roll back or block the core transaction. Builds `EventGridPublisherClient` from non-secret `EventGrid:TopicEndpoint` + `DefaultAzureCredential` (Entra, no topic key); a missing/invalid endpoint makes it a safe no-op. Registered via `AddEventGridSideEffectPublisher()`. See [docs/design/EVENTBUS.md](docs/design/EVENTBUS.md) "Two Eventing Mechanisms"
- `Resilience/ResilienceExtensions` — `AddHttpResilienceWithCircuitBreaker()`, `AddRedisResilience()`, `AddNpgsqlResilience()` (Npgsql uses exponential backoff + jitter to prevent thundering herd on DB reconnect); `AddDataStoreRetry()` / `AddDataStoreResiliencePipeline()` — driver-agnostic data-store retry that **honours a server-supplied Retry-After** (verbatim, no jitter) and falls back to exponential backoff + jitter; the caller passes `isTransient`/`getRetryAfter` delegates so no data-store driver leaks into BuildingBlocks (used by Products for Cosmos 429 throttling). **Criticality-tiered:** `AddOptionalDependencyResilience()` is the fail-fast counterpart for OPTIONAL dependencies — no retry + a quick-opening circuit breaker + short timeout, so a down optional service degrades silently instead of slowing the core request (used by the Products → Discount gRPC client; contrast the patient retries for critical Cosmos/Service Bus)
- `Swagger/SwaggerExtensions` — `UseSwaggerInDevelopment(title)` gates `UseSwagger()` + `UseSwaggerUI()` to Development only; `AddSwaggerGen()` registration stays in each service
- `Versioning/ApiVersioningExtensions` — `AddStandardApiVersioning()` sets default v1.0, accepts version via URL segment (`/api/v1/`) or `api-version` header; currently demonstrated in AK.Order, other services adopt by calling this single method

### ✅ AK.IntegrationTests  (Event Bus Tests)
- **Framework:** MassTransit in-memory test harness (no broker, no DB, no host) — transport-agnostic
- **Tests:** 28 passing — order saga, order event bus, payment event bus, payment happy-path, payment sad-path (notification consumer tests were removed with the old microservice — notifications are serverless)
- **Design doc:** [AK.IntegrationTests/INTEGRATION_TESTS.md](AK.IntegrationTests/INTEGRATION_TESTS.md)

### ✅ AK.Notification.Functions  (Azure Functions — .NET 9 isolated)
- **Runtime:** Azure Functions v4, **.NET 9 isolated worker** (`OutputType=Exe`, `HostBuilder().ConfigureFunctionsWorkerDefaults()`)
- **Triggers:** one `[EventGridTrigger]` Function per customer notification event in `NotificationFunctions` — `OnOrderCreated`, `OnOrderConfirmed`, `OnOrderCancelled`, `OnPaymentSucceeded`, `OnPaymentFailed` (Event Grid topic `evgt-antkart-dev`); deployed to the provisioned Function App `func-antkart-notifications-dev`
- **Role in the platform:** the push-based, scale-to-zero half of the **two-mechanism eventing model** — discrete fire-and-forget notifications, deliberately separate from the durable Service Bus saga
- **Auth:** no secrets — reaches Azure resources (ACS, Key Vault) via its **managed identity** (`DefaultAzureCredential`)
- **Behaviour (thin):** each Function only (1) deserializes the shared event contract (`AK.BuildingBlocks.Messaging.Notifications`) and (2) builds a `NotificationRequest` and calls `INotificationDispatcher.DispatchAsync`. ALL logic (templates, ACS email channel, history persistence, failure handling) lives in **`AK.Notification.Core`** behind the dispatcher (which never throws on a delivery failure — it records it). A malformed payload is logged and skipped, not crash-looped.
- **Program.cs:** `ConfigureAppConfiguration` folds in Key Vault (secret-less, `DefaultAzureCredential`, no-op locally) + `AddNotificationCore(configuration)` (the single call that wires dispatcher + Email channel + templates + EF history store/DbContext)
- **Packages:** `Microsoft.Azure.Functions.Worker` 2.0.0, `Microsoft.Azure.Functions.Worker.Sdk` 2.0.0, `Microsoft.Azure.Functions.Worker.Extensions.EventGrid` 3.4.3; references `AK.Notification.Core` + `AK.BuildingBlocks`
- **Location:** lives in the `AK.Notification/` service folder (`AK.Notification.Functions`, sibling of `AK.Notification.Core`); it remains its own separate deployable
- **Tests:** the 6 Function tests live in the single `AK.Notification.Tests` project (under its `Functions/` subfolder) — each Function deserializes its event and dispatches the correct `NotificationRequest` (mock `INotificationDispatcher`); malformed payload → no dispatch. Design recorded in [ADR-019](docs/adr/ADR-019-serverless-notification-functions-eventgrid.md)

### ✅ AK.Payments  (REST Minimal API)
- **Transport:** HTTP REST, port 5086 (dev) / 8085 (Docker)
- **Database:** PostgreSQL — `AKPaymentsDb` via EF Core 9 + Npgsql, code-first migrations
- **External:** Razorpay sandbox (test cards: 4111 1111 1111 1111 Visa, 5267 3169 4984 2643 Mastercard; OTP: 1234 1234)
- **Razorpay credentials (secret-less):** `AK.Payments.API` startup calls `AddAzureKeyVaultConfiguration` (same as Products); the sandbox keys are vaulted as `Razorpay--KeyId` / `Razorpay--KeySecret`, projected to `Razorpay:KeyId` / `Razorpay:KeySecret` and bound to `RazorpaySettings`. `appsettings*.json` hold only empty placeholders — **no real keys committed**; runtime auth via `DefaultAzureCredential` (managed identity)
- **Architecture:** DDD + Clean Architecture
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Repository, Unit of Work, EF Core Outbox
- **Operations:** Initiate payment, verify signature, saved cards CRUD, user payment history
- **Saved cards:** PCI-compliant — Razorpay token IDs only, never raw card numbers
- **Domain events:** `PaymentCreatedEvent`, `PaymentSucceededEvent`, `PaymentFailedEvent` — all implement `AK.BuildingBlocks.DDD.IDomainEvent`; inherited `Entity` base class holds the typed event list
- **Integration events:** Publishes `PaymentInitiatedIntegrationEvent`, `PaymentSucceededIntegrationEvent`, `PaymentFailedIntegrationEvent`; AK.Order consumes succeeded/failed to update order status
- **Event Grid notification side-effects (commit-then-notify):** `VerifyPaymentCommandHandler` publishes the shared `PaymentSucceededNotification` / `PaymentFailedNotification` (BuildingBlocks) via `IEventGridSideEffectPublisher.TryPublishAsync` (never-throws), **strictly after the durable commit** — a publish failure can't roll back or fail the payment. `customerEmail` is already a field on the `Payment` entity (captured at InitiatePayment), so it's available at the publish point with no extra lookup. Registers `AddEventGridSideEffectPublisher()`; non-secret `EventGrid:TopicEndpoint` in `appsettings`
- **Tests:** 73 passing (domain, commands, queries, validators — SaveCard, DeleteSavedCard, GetPaymentById, GetPaymentByOrderId, GetUserPayments, GetUserSavedCards, VerifyPayment incl. PaymentSucceeded/Failed notification publish + publish-failure tolerance, SaveCard validators)
- **Swagger:** `http://localhost:5086/swagger` (Development only)
- **Removed:** Unimplemented `/api/payments/webhook` stub endpoint was removed
- **Design doc:** [AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md)

### ✅ AK.Notification — serverless (Event Grid + Functions)
Notifications are a **pure serverless** capability: there is no notification microservice host or Service Bus subscription. The old REST microservice (`AK.Notification.API`/`.Application`/`.Infrastructure`/`.Domain` + their tests) and the Service Bus `notification` subscription were **removed** once the serverless path fully replaced them. The capability is now:
- **`AK.Notification.Core`** (reusable library) — the channel abstraction (`NotificationChannelType` Email/WhatsApp/Sms, `INotificationChannel` → `NotificationSendResult`, `EmailNotificationChannel` wrapping the shared ACS `IEmailSender`), message/request models, one template per `NotificationType` (OrderCreated/OrderConfirmed/OrderCancelled/PaymentSucceeded/PaymentFailed) resolved by type, `INotificationDispatcher` (resolve template → compose → send per channel → write one `NotificationHistory` audit row per attempt; never throws on a delivery failure — records it), an EF Core `NotificationHistoryDbContext` + `InitialCreate` migration (secret-less Npgsql; design-time factory), and `AddNotificationCore(configuration)`. Adding a channel = implement `INotificationChannel` + register it (Open/Closed). **17 core tests** — in the single `AK.Notification.Tests` project under its `Core/` subfolder (templates, email channel, dispatcher send/persist/failure, EF history persist — EF InMemory + mocks).
- **`AK.Notification.Functions`** (Azure Functions, see its own section) — Event Grid-triggered, dispatch through the Core.
- **Publishers** — AK.Order and AK.Payments emit the five `AK.BuildingBlocks.Messaging.Notifications` events to Event Grid as fire-and-forget side-effects after each durable commit.

---

## Security Conventions

### User Identity — Never Trust the Client
- **Never** accept `userId` as a URL path parameter or request body field for user-scoped operations
- **Always** derive the authenticated user's ID from the JWT via `http.GetUserId()` (BuildingBlocks `HttpContextExtensions`)
- `GetUserId()` reads the `sub` claim (the caller's stable id in the Entra token); falls back to `preferred_username`; throws `UnauthorizedAccessException` if neither is present
- `UnauthorizedAccessException` maps to HTTP 403 in all `ExceptionHandlerMiddleware` implementations

### Route Patterns for User-Scoped Data
| Service | Pattern | Example |
|---------|---------|---------|
| ShoppingCart | `/api/v1/cart` (no userId in path) | `GET /api/v1/cart` |
| Order | `/api/orders/me` for current user | `GET /api/orders/me` |
| Payments | `/api/payments/me` for current user | `GET /api/payments/me` |
| SavedCards | `/api/payments/cards` (no userId in path) | `GET /api/payments/cards` |

### Ownership Checks
- Order `GET /{id}` and `DELETE /{id}`: return 403 if `order.UserId != http.GetUserId()` and caller is not admin
- `PUT /{id}/status` requires `.RequireAuthorization("admin")`
- SavedCard `DELETE /{id}`: handler verifies ownership against JWT userId before deleting

### Request DTOs at Endpoint Layer
- `InitiatePaymentRequest` (PaymentEndpoints) and `SaveCardRequest` (SavedCardEndpoints) are endpoint-layer records with no `userId` field — prevents clients from spoofing identity in the request body

---

## Architecture Rules

### Folder & Naming
- One top-level folder per microservice: `AK.<ServiceName>/`
- Projects inside are flat — no double-nesting: `AK.<Service>/AK.<Service>.Domain/` not `AK.<Service>/AK.<Service>.Domain/AK.<Service>.Domain/`
- New services follow the same pattern and are registered in `AntKart.sln`
- Design doc lives inside the service folder: `AK.<Service>/<SERVICE>_TECHNICAL_DESIGN.md`
- Link every new design doc from `README.md`

### Layer Dependencies (enforced by ProjectReference)
```
Domain        ← no dependencies on other layers
Application   ← Domain, AK.BuildingBlocks
Infrastructure ← Application (and Domain transitively)
API / Grpc    ← Application, Infrastructure, AK.BuildingBlocks
Tests         ← Application, Infrastructure, Domain (no API/Grpc)
```

### Cross-Cutting Concerns
- **Always** reference `AK.BuildingBlocks` from Application and API/Grpc layers
- **Never** duplicate `PagedResult<T>`, `Result<T>`, or exception types — use BuildingBlocks
- **Always** add `SerilogExtensions.AddSerilogLogging()` in `Program.cs`
- **Always** add `AddDefaultHealthChecks()` + `MapDefaultHealthChecks()` in `Program.cs`

---

## Coding Conventions

### General
- Target `net9.0` for all projects
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in every `.csproj`
- No comments unless the WHY is non-obvious (hidden constraint, workaround, subtle invariant)
- No XML doc comments / multi-line docstrings
- No `[ApiController]` — use Minimal API endpoint classes

### CQRS / MediatR
- Commands return DTOs or primitives — never domain entities
- Queries return DTOs or `PagedResult<TDto>`
- `ValidationBehavior<TRequest, TResponse>` wired in `AddApplication()` extension
- Register MediatR with `cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly)`

### Entities / Domain
- Domain entities: private setters, factory method `Create(...)`, no EF Core attributes
- EF Core config (Infrastructure): fluent API in `OnModelCreating`, never data annotations on domain entities
- MongoDB entities: no Bson attributes on domain entities — register `BsonClassMap` in Infrastructure instead; use `Guid.NewGuid().ToString("N")` for IDs
- No cross-service entity references — denormalise (copy) fields needed from other services

### DTOs
- DTOs are `record` types in the Application layer
- Mappers are `internal static` extension method classes (`XMapper.cs`) — no AutoMapper
- Never return domain entities from handlers or endpoints

### Validation
- FluentValidation validators in `Application/Validators/`
- `ValidationBehavior` pipeline throws `FluentValidation.ValidationException` on failure
- `ExceptionHandlerMiddleware` (REST) or `ExceptionInterceptor` (gRPC) maps exceptions to correct HTTP/gRPC status codes

### Error Mapping

| Exception | HTTP Status | gRPC Status |
|-----------|-------------|-------------|
| `FluentValidation.ValidationException` | 400 | `InvalidArgument` |
| `UnauthorizedAccessException` | 403 | `PermissionDenied` |
| `KeyNotFoundException` | 404 | `NotFound` |
| `InvalidOperationException` | 409 | `AlreadyExists` |
| `Exception` (catch-all) | 500 | `Internal` |

### Tests
- Framework: XUnit + Moq + FluentAssertions
- All tests are pure unit tests — no database, no network, no running host
- Mock via interface (`Mock<IRepository>`) — never mock concrete classes
- `TestDataFactory` static class provides all test data builders
- Test project references: Application, Infrastructure, Domain — never API/Grpc layer
- `<Using Include="Xunit" />` global using in test `.csproj`
- Naming: `Method_Condition_ExpectedResult`

---

## NuGet Configuration

**Critical:** A private Azure DevOps feed in the global NuGet config returns 401. The `nuget.config` at repo root overrides it:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

Always run `dotnet restore` from the repo root so this config is picked up. Never add `--ignore-failed-sources`.

### Approved Package Versions

| Package | Version | Used In |
|---------|---------|---------|
| MediatR | 12.4.1 | Application |
| FluentValidation | 11.x | Application |
| FluentValidation.DependencyInjectionExtensions | 11.x | Application |
| MongoDB.Driver | 3.3.0 | Products Infrastructure |
| Microsoft.EntityFrameworkCore.Sqlite | 9.x | Discount Infrastructure |
| Grpc.AspNetCore | 2.x | Discount Grpc |
| Swashbuckle.AspNetCore | 7.x | Products API |
| Serilog.AspNetCore | 7.x | BuildingBlocks |
| Serilog.Sinks.Elasticsearch | 9.0.3 | BuildingBlocks |
| MassTransit | 8.3.6 | BuildingBlocks, Order, Products, ShoppingCart |
| MassTransit.Azure.ServiceBus.Core | 8.3.6 | BuildingBlocks (Azure Service Bus transport, Entra auth) |
| MassTransit.EntityFrameworkCore | 8.3.6 | Order Infrastructure (outbox + saga) |
| Azure.Identity | 1.13.2 | BuildingBlocks (Key Vault + Service Bus token auth) |
| Azure.Security.KeyVault.Secrets | 4.7.0 | BuildingBlocks (Key Vault deep health check) |
| Azure.Messaging.EventGrid | 4.30.0 | BuildingBlocks (fire-and-forget side-effect publisher) |
| Azure.Communication.Email | 1.0.1 | BuildingBlocks (ACS email sender) |
| CsvHelper | 33.0.1 | AK.Tools.ProductsSeedGenerator / ProductsSeedLoader (seed CSV) |
| Microsoft.Azure.Functions.Worker | 2.0.0 | AK.Notification.Functions |
| Microsoft.Azure.Functions.Worker.Sdk | 2.0.0 | AK.Notification.Functions |
| Microsoft.Azure.Functions.Worker.Extensions.EventGrid | 3.4.3 | AK.Notification.Functions |
| Microsoft.Extensions.Http.Resilience | 9.0.0 | BuildingBlocks, Products Infrastructure |
| Microsoft.Extensions.Resilience | 9.0.0 | BuildingBlocks, Order/ShoppingCart/Products Infrastructure |
| Ocelot | 23.4.2 | Gateway API |
| Razorpay | 3.3.2 | Payments Infrastructure |
| xunit | 2.9.x | Tests |
| Moq | 4.20.x | Tests |
| FluentAssertions | 7.x | Tests |

**Do NOT add:** `MediatR.Extensions.Microsoft.DependencyInjection` (removed in v12), `Microsoft.AspNetCore.OpenApi` (conflicts with Swashbuckle), `MassTransit.Testing` (not a separate package — test harness is in `MassTransit` itself).

---

## Build & Test Commands

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run tests for one service
dotnet test AK.Products/AK.Products.Tests/AK.Products.Tests.csproj
dotnet test AK.Discount/AK.Discount.Tests/AK.Discount.Tests.csproj

# Add EF Core migration (Discount service)
dotnet ef migrations add <MigrationName> \
  --project AK.Discount/AK.Discount.Infrastructure \
  --startup-project AK.Discount/AK.Discount.Grpc

# Run individual services (dev)
cd AK.Products/AK.Products.API && dotnet run   # → http://localhost:5077/swagger
cd AK.Discount/AK.Discount.Grpc && dotnet run  # → grpc://localhost:5001
```

---

## Adding a New Microservice — Checklist

When asked to build a new service `AK.<Name>`, follow this order:

1. **Create folder structure:**
   ```
   AK.<Name>/
     AK.<Name>.Domain/
     AK.<Name>.Application/
     AK.<Name>.Infrastructure/
     AK.<Name>.API  or  AK.<Name>.Grpc/
     AK.<Name>.Tests/
   ```

2. **Create `.csproj` files** with correct `ProjectReference` paths (relative, single `..` level within the service folder, `../..` to reach `AK.BuildingBlocks`)

3. **Register all projects in solution:**
   ```bash
   dotnet sln add AK.<Name>/AK.<Name>.Domain/AK.<Name>.Domain.csproj ...
   ```

4. **Implement layers** in order: Domain → Application → Infrastructure → API/Grpc → Tests

5. **Wire up `Program.cs`:** Serilog, health checks, MediatR, validators, infrastructure, seed

6. **Add Dockerfile** inside the API/Grpc project folder

7. **Add the service to the cloud deployment configuration** (this repository targets cloud deployment; there is no local docker-compose stack)

8. **Run `dotnet build`** — must succeed with 0 errors before proceeding

9. **Run `dotnet test`** — all tests must pass

10. **Create `<SERVICE>_TECHNICAL_DESIGN.md`** inside the service folder

11. **Update `README.md`** — add row to the Microservices table, link to design doc

12. **Update `AntKart.postman_collection.json`** — add new service folder with all requests

13. **Update `CLAUDE.md`** — add the service to the Completed Services section

14. **Commit** with message: `Add AK.<Name> microservice`

---

## Docker

- Build context is always the **repo root** (`.`)
- Dockerfiles live inside the API/Grpc project folder
- **Non-root containers:** All Dockerfiles include `USER $APP_UID` before `ENTRYPOINT` — uses .NET 9 base image UID 1654
- **Container/image naming:** use the `antkart-` prefix (e.g. `antkart-mongodb`, `antkart-rabbitmq`) — never `antcart-` or `ak-`
- **Seeding:** Startup auto-seeding is opt-in and resilient — gated behind the `Seeding:RunOnStartup` flag (default `false`) and wrapped in try/catch so a seed failure logs a warning and never crashes startup. Routine data seeding is a deliberate, separate operation (dedicated loader), not a boot-time side effect.

> **No local docker-compose stack.** This repository targets cloud deployment — run services locally against live cloud services or via cloud port-forwarding. The docker-compose-based Phase-1 local orchestration (compose files, `.dockerignore`, healthchecks, `depends_on` wiring, named volumes) is preserved in the public AntKart reference repository.

---

## Postman Collection

Single file: `AntKart.postman_collection.json` at repo root.

- One top-level folder per microservice
- Collection variables: `productsUrl`, `discountGrpc` — add `<serviceName>Url` for each new service
- gRPC services: include grpcurl commands as request descriptions (Postman gRPC tab also supported)
- When adding a new REST service, add its requests as a new folder; do not create separate collection files

---

## README Updates

`README.md` must always reflect the current state:
- **Solution Structure** tree — add new service folder
- **Microservices** table — one row per service (transport, database, design doc link)
- **Tests** table — add test count
- All links must use relative paths (not `../` outside the repo)
