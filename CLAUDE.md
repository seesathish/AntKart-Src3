# AntKart — Claude Code Instructions

This file guides Claude Code on conventions, architecture, and rules for the AntKart microservices platform. Read this fully before starting any task.

---

## Platform Overview

AntKart is a cloud-native e-commerce platform built as independently deployable .NET 9 microservices. All services live in a single Git repository (`AntKart.sln`) organised as one top-level folder per microservice.

---

## Repository Layout

```
AntKart/
├── AK.Products/          REST Minimal API — product catalogue (MongoDB)
├── AK.Discount/          gRPC service — discount coupons (SQLite)
├── AK.ShoppingCart/      REST Minimal API — shopping cart (Redis)
├── AK.Order/             REST Minimal API — order management (PostgreSQL + SAGA)
├── AK.Gateway/           API Gateway — Ocelot single entry point
├── AK.Payments/          REST Minimal API — payment processing (PostgreSQL + Razorpay)
├── AK.Notification/      REST Minimal API — transactional notifications (PostgreSQL + Mailhog/SMTP)
├── AK.BuildingBlocks/    Shared cross-cutting library (no business logic)
├── AK.IntegrationTests/  SAGA + event bus + notification consumer tests (no API/Grpc dependency)
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
- **Database:** MongoDB — `AKProductsDb` / `Products` collection
- **Architecture:** DDD + Clean Architecture — `MongoDB.Driver` confined to Infrastructure only; Domain has zero infrastructure dependencies
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Specification, Unit of Work
- **Category design:** Data-driven — `CategoryName` (top-level: Men/Women/Kids/Sports/etc.) + `SubCategoryName` (specific: Shirts/Dresses/etc.) are plain strings; no hardcoded enum. Adding a new category is a data change only.
- **ID format:** `Guid.NewGuid().ToString("N")` — 32-char hex string, pure .NET, stored as BSON string
- **MongoDB mapping:** `ProductClassMap` (Infrastructure) registers `BsonClassMap<StringEntity>` and `BsonClassMap<Product>` — handles `SetIgnoreExtraElements` and unmaps `DomainEvents`; called before `MongoDbContext` is registered
- **Seed data:** 300 products driven by `CategoryDefinition` record array — 10 sub-categories × 10 products per top-level category. Currently seeded: Men, Women, Kids.
- **SKU format:** `{CAT_ABBREV}-{SUBCAT_ABBREV}-{001..NNN}` e.g. `MEN-SHIR-001`, `WOM-DRES-001`
- **New endpoint:** `GET /api/v1/products/categories` — returns distinct top-level category names from DB
- **Removed endpoints:** `/men`, `/women`, `/kids` — replaced by `?category=Men`, `?category=Women`, `?category=Kids`
- **Tests:** 202 passing (domain, commands, queries, validators, specifications, DTO mapping, GetProductCategories handler, ReserveStockConsumer)
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
- **Tests:** 113 passing (domain, features, validators, behaviors, infrastructure with EF InMemory)
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
- `Logging/SerilogExtensions` — Serilog with console + rolling file + Elasticsearch sink
- `HealthChecks/HealthCheckExtensions` — `/health` endpoint
- `Middleware/CorrelationIdMiddleware` — `X-Correlation-Id` header
- `Authentication/AuthenticationExtensions` — `AddEntraAuthentication()` + `UseEntraAuth()` shared JWT auth wiring; validates the token against Microsoft Entra ID (issuer = tenant v2 issuer, audience = the API app registration `api://antkart-api-dev`, lifetime, signature via Entra OIDC keys) and reads authorization from the **flat `roles` claim** (`RoleClaimType = "roles"`, `MapInboundClaims = false`)
- `Authentication/EntraSettings` — typed, non-secret config record (`Instance`, `TenantId`, `Audience`) bound from the `Entra` section; derives authority/issuer as `{Instance}/{TenantId}/v2.0`
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
- `Resilience/ResilienceExtensions` — `AddHttpResilienceWithCircuitBreaker()`, `AddRedisResilience()`, `AddNpgsqlResilience()` (Npgsql uses exponential backoff + jitter to prevent thundering herd on DB reconnect)
- `Swagger/SwaggerExtensions` — `UseSwaggerInDevelopment(title)` gates `UseSwagger()` + `UseSwaggerUI()` to Development only; `AddSwaggerGen()` registration stays in each service
- `Versioning/ApiVersioningExtensions` — `AddStandardApiVersioning()` sets default v1.0, accepts version via URL segment (`/api/v1/`) or `api-version` header; currently demonstrated in AK.Order, other services adopt by calling this single method

### ✅ AK.IntegrationTests  (Event Bus Tests)
- **Framework:** MassTransit in-memory test harness (no broker, no DB, no host) — transport-agnostic
- **Tests:** 35 passing — 6 order saga, 4 order event bus, 7 payment event bus, 5 payment happy-path, 6 payment sad-path, 7 notification consumers
- **Design doc:** [AK.IntegrationTests/INTEGRATION_TESTS.md](AK.IntegrationTests/INTEGRATION_TESTS.md)

### ✅ AK.Payments  (REST Minimal API)
- **Transport:** HTTP REST, port 5086 (dev) / 8085 (Docker)
- **Database:** PostgreSQL — `AKPaymentsDb` via EF Core 9 + Npgsql, code-first migrations
- **External:** Razorpay sandbox (test cards: 4111 1111 1111 1111 Visa, 5267 3169 4984 2643 Mastercard; OTP: 1234 1234)
- **Architecture:** DDD + Clean Architecture
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Repository, Unit of Work, EF Core Outbox
- **Operations:** Initiate payment, verify signature, saved cards CRUD, user payment history
- **Saved cards:** PCI-compliant — Razorpay token IDs only, never raw card numbers
- **Domain events:** `PaymentCreatedEvent`, `PaymentSucceededEvent`, `PaymentFailedEvent` — all implement `AK.BuildingBlocks.DDD.IDomainEvent`; inherited `Entity` base class holds the typed event list
- **Integration events:** Publishes `PaymentInitiatedIntegrationEvent`, `PaymentSucceededIntegrationEvent`, `PaymentFailedIntegrationEvent`; AK.Order consumes succeeded/failed to update order status
- **Tests:** 70 passing (domain, commands, queries, validators — SaveCard, DeleteSavedCard, GetPaymentById, GetPaymentByOrderId, GetUserPayments, GetUserSavedCards, VerifyPayment, SaveCard validators)
- **Swagger:** `http://localhost:5086/swagger` (Development only)
- **Removed:** Unimplemented `/api/payments/webhook` stub endpoint was removed
- **Design doc:** [AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md)

### ✅ AK.Notification  (REST Minimal API — Event-driven)
- **Transport:** HTTP REST, port 5087 (dev) / 8086 (Docker)
- **Database:** PostgreSQL — `AKNotificationsDb` via EF Core 9 + Npgsql, auto-migrates on startup
- **Email (local dev):** Mailhog SMTP trap — port 1025 (SMTP), port 8025 (web UI at `http://localhost:8025`)
- **Email (production):** Gmail SMTP via `antkartadmin@gmail.com`; credentials supplied through the notification service's `EmailSettings` (kept out of source control); `EmailSettings__Password` must be a Gmail App Password (not the account password)
- **Email SSL:** `EmailNotificationChannel` uses explicit `SecureSocketOptions` — port 465 → `SslOnConnect`, port 587 → `StartTls`, Mailhog/plain → `None`; never pass a bare `bool` to `ConnectAsync`
- **Architecture:** DDD + Clean Architecture (Domain → Application → Infrastructure → API)
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation, MassTransit consumers, channel abstraction
- **Channel abstraction:** `INotificationChannel` interface resolved by `INotificationChannelResolver`; Email fully implemented (MailKit); SMS + WhatsApp are stubbed for future activation
- **Events consumed:** `OrderCreatedIntegrationEvent` (order confirmation), `OrderConfirmedIntegrationEvent` (stock confirmed), `OrderCancelledIntegrationEvent` (cancellation notice), `PaymentSucceededIntegrationEvent` (payment receipt), `PaymentFailedIntegrationEvent` (payment failure alert)
- **Retention:** `NotificationCleanupService` (BackgroundService) deletes notifications older than 90 days, runs daily at 02:00 UTC
- **Gateway routes:** `GET /gateway/notifications/{everything}` — authenticated, 20 RPS rate limit
- **Tests:** 37 passing (domain, command handler, query handlers, consumers, template renderer)
- **Swagger:** `http://localhost:5087/swagger` (Development only)
- **Design doc:** [AK.Notification/NOTIFICATION_TECHNICAL_DESIGN.md](AK.Notification/NOTIFICATION_TECHNICAL_DESIGN.md)

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
| Microsoft.Extensions.Http.Resilience | 9.0.0 | BuildingBlocks, Products Infrastructure |
| Microsoft.Extensions.Resilience | 9.0.0 | BuildingBlocks, Order/ShoppingCart Infrastructure |
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
