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
├── AK.UserIdentity/      REST Minimal API — Keycloak identity proxy
├── AK.Gateway/           API Gateway — Ocelot single entry point
├── AK.Payments/          REST Minimal API — payment processing (PostgreSQL + Razorpay)
├── AK.BuildingBlocks/    Shared cross-cutting library (no business logic)
├── AK.IntegrationTests/  SAGA + event bus tests (no API/Grpc dependency)
├── AntKart.sln
├── AntKart.postman_collection.json
├── docker-compose.yml
├── docker-compose.override.yml
├── EVENTBUS.md           Event bus & SAGA design
├── RESILIENCE.md         Polly circuit breaker design
├── OBSERVABILITY.md      ELK observability design
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
- **MongoDB mapping:** `ProductClassMap` (Infrastructure) registers `BsonClassMap` — handles `SetIgnoreExtraElements` and unmaps `DomainEvents`; called before `MongoDbContext` is registered
- **Seed data:** 300 products driven by `CategoryDefinition` record array — 10 sub-categories × 10 products per top-level category. Currently seeded: Men, Women, Kids.
- **SKU format:** `{CAT_ABBREV}-{SUBCAT_ABBREV}-{001..NNN}` e.g. `MEN-SHIR-001`, `WOM-DRES-001`
- **New endpoint:** `GET /api/v1/products/categories` — returns distinct top-level category names from DB
- **Removed endpoints:** `/men`, `/women`, `/kids` — replaced by `?category=Men`, `?category=Women`, `?category=Kids`
- **Tests:** 190 passing (domain, commands, queries, validators, specifications, DTO mapping)
- **Swagger:** `http://localhost:5077/swagger`
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
- **Swagger:** `http://localhost:5079/swagger`
- **Design doc:** [AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md)

### ✅ AK.Order  (REST Minimal API)
- **Transport:** HTTP REST, port 5080 (dev) / 8083 (Docker)
- **Database:** PostgreSQL — `AKOrdersDb` via EF Core 9 + Npgsql, code-first migrations
- **Architecture:** Vertical Slice Clean Architecture (features organised by slice in Application layer)
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Specification, Repository, Unit of Work
- **Operations:** Create order, get order by ID, list orders (paged, filtered), list by user, update status, cancel
- **Order number format:** `ORD-{yyyyMMdd}-{8-char-GUID-uppercase}` e.g. `ORD-20260418-A1B2C3D4`
- **Domain events:** `OrderCreatedEvent`, `OrderStatusChangedEvent`, `OrderCancelledEvent`
- **Tests:** 106 passing (domain, features, validators, behaviors, infrastructure with EF InMemory)
- **Swagger:** `http://localhost:5080/swagger`
- **Design doc:** [AK.Order/ORDER_TECHNICAL_DESIGN.md](AK.Order/ORDER_TECHNICAL_DESIGN.md)

### ✅ AK.UserIdentity  (REST Minimal API — Keycloak Proxy)
- **Transport:** HTTP REST, port 5085 (dev) / 8084 (Docker)
- **Identity Provider:** Keycloak 24.0 — realm `antkart`, client `antkart-client` (confidential, service accounts enabled)
- **Architecture:** Single API project — thin proxy, no domain layer needed
- **Roles:** `user` (standard), `admin` (full access)
- **Endpoints:** POST /login, POST /register, POST /refresh, GET /me, GET /admin/users, POST /admin/users/{id}/roles
- **Auth:** JWT Bearer validated against Keycloak OIDC discovery endpoint
- **Tests:** 15 passing (KeycloakService, KeycloakAdminService, ExceptionHandlerMiddleware — all mocked HTTP)
- **Swagger:** `http://localhost:5085/swagger`
- **Design doc:** [AK.UserIdentity/IDENTITY_TECHNICAL_DESIGN.md](AK.UserIdentity/IDENTITY_TECHNICAL_DESIGN.md)

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
- `Authentication/AuthenticationExtensions` — `AddKeycloakAuthentication()` + `UseKeycloakAuth()` shared JWT auth wiring
- `Authentication/KeycloakSettings` — typed config record for Keycloak settings
- `Messaging/IIntegrationEvent` — base interface for all integration events
- `Messaging/IntegrationEvents/` — `OrderCreatedIntegrationEvent`, `StockReservedIntegrationEvent`, `StockReservationFailedIntegrationEvent`, `OrderConfirmedIntegrationEvent`, `OrderCancelledIntegrationEvent`, `CartClearedIntegrationEvent`
- `Messaging/MassTransitExtensions` — `AddRabbitMqMassTransit()` helper (kebab-case, global retry)
- `Resilience/ResilienceExtensions` — `AddHttpResilienceWithCircuitBreaker()`, `AddRedisResilience()`, `AddNpgsqlResilience()`

### ✅ AK.IntegrationTests  (Event Bus Tests)
- **Framework:** MassTransit in-memory test harness (no RabbitMQ, no DB, no host)
- **Tests:** 28 passing — 6 order saga, 4 order event bus, 7 payment event bus, 5 payment happy-path, 6 payment sad-path
- **Design doc:** [AK.IntegrationTests/INTEGRATION_TESTS.md](AK.IntegrationTests/INTEGRATION_TESTS.md)

### ✅ AK.Payments  (REST Minimal API)
- **Transport:** HTTP REST, port 5086 (dev) / 8085 (Docker)
- **Database:** PostgreSQL — `AKPaymentsDb` via EF Core 9 + Npgsql, code-first migrations
- **External:** Razorpay sandbox (test cards: 4111 1111 1111 1111 Visa, 5267 3169 4984 2643 Mastercard; OTP: 1234 1234)
- **Architecture:** DDD + Clean Architecture
- **Patterns:** CQRS (MediatR 12.4.1), FluentValidation pipeline, Repository, Unit of Work, EF Core Outbox
- **Operations:** Initiate payment, verify signature, saved cards CRUD, user payment history
- **Saved cards:** PCI-compliant — Razorpay token IDs only, never raw card numbers
- **Integration events:** Publishes `PaymentInitiatedIntegrationEvent`, `PaymentSucceededIntegrationEvent`, `PaymentFailedIntegrationEvent`; AK.Order consumes succeeded/failed to update order status
- **Tests:** 28 passing (domain, commands, validators)
- **Swagger:** `http://localhost:5086/swagger`
- **Design doc:** [AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md)

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
| MassTransit.RabbitMQ | 8.3.6 | Infrastructure (event bus services) |
| MassTransit.EntityFrameworkCore | 8.3.6 | Order Infrastructure (outbox + saga) |
| Microsoft.Extensions.Http.Resilience | 9.0.0 | BuildingBlocks, Products Infrastructure |
| Microsoft.Extensions.Resilience | 9.0.0 | BuildingBlocks, Order/ShoppingCart Infrastructure |
| Ocelot | 23.4.2 | Gateway API |
| Razorpay | 3.1.0 | Payments Infrastructure |
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

# Docker Compose (all services)
docker-compose up --build
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

7. **Add service to `docker-compose.yml`** and `docker-compose.override.yml`

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
- SQLite DB for Discount must persist via a named volume (`discount_data:/app/data`)
- MongoDB must be running for Products to seed on startup
- Services that depend on MongoDB must have `depends_on: - mongodb` in compose

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
