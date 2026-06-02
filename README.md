# AntKart

AntKart is a cloud-native e-commerce platform built as independently deployable .NET 9 microservices with Clean Architecture, DDD, CQRS, Event Bus (SAGA), API Gateway, Resilience, and full Observability.

---

## Architecture Overview

### AntKart Cloud Architecture

> 📌 _Placeholder — the AntKart cloud architecture diagram will be added here._

### DevOps Architecture

> 📌 _Placeholder — the DevOps (CI/CD) architecture diagram will be added here._

### C4 Architecture Diagrams

> 📌 _The C4 diagrams below are to be updated._

#### Level 1 — System Context

AntKart as a single system, with two actors (Customer, Administrator) and five external dependencies: Keycloak (identity), Razorpay (payments), SMTP (email), RabbitMQ (messaging), and ELK (observability).

![Level 1 — System Context](docs/architecture/c4-level1-system-context.png)

#### Level 2 — Container

Eight independently deployable microservices behind an Ocelot API Gateway. Each service owns its database — MongoDB (Products), PostgreSQL (Orders, Payments, Notifications), Redis (Cart), SQLite (Discount). Services communicate asynchronously over RabbitMQ via MassTransit, except AK.Discount which is called synchronously over gRPC.

![Level 2 — Container](docs/architecture/c4-level2-container.png)

#### Level 3 — Component: AK.Order

AK.Order is the most architecturally rich service — CQRS via MediatR, SAGA orchestration with MassTransit, EF Core Outbox for guaranteed event delivery, and a domain model with an enforced state machine. Commands flow through a `ValidationBehavior` pipeline; `CancelOrder` and `UpdateOrderStatus` return `Result<T>` for expected failures while `CreateOrder` uses exceptions for unexpected ones.

![Level 3 — Component: AK.Order](docs/architecture/c4-level3-order-components.png)

#### Order Flow — Dynamic View

The end-to-end order journey: Customer → Gateway → Order (creates via Outbox) → RabbitMQ → Products (reserves stock) → SAGA confirms → Payment initiated → Razorpay verifies → Payment succeeded → Order updated to Paid → Notification emails sent at each stage.

![Order Flow — Dynamic View](docs/architecture/c4-order-flow-dynamic.png)

### Architecture Highlights

- **Clean Architecture + DDD per service** — each microservice has Domain, Application, Infrastructure, and API layers with strict inward dependency rules; domain entities use private setters and factory methods with no framework leakage.
- **CQRS via MediatR 12 in every service** — commands and queries are fully separated; a `ValidationBehavior<TRequest, TResponse>` pipeline ensures all requests are validated by FluentValidation before reaching handlers.
- **MassTransit SAGA orchestrates order → stock → payment → notification** — the `OrderSaga` in AK.Order transitions through `Initial → StockPending → Confirmed/Cancelled` states, coordinating AK.Products, AK.ShoppingCart, AK.Payments, and AK.Notification over RabbitMQ without any direct service-to-service HTTP calls.
- **AK.Notification is fully event-driven** — consumes six integration events (`UserRegistered`, `OrderCreated`, `OrderConfirmed`, `OrderCancelled`, `PaymentSucceeded`, `PaymentFailed`) and dispatches transactional emails via MailKit. Local dev uses an SMTP trap (e.g. Mailhog at `http://localhost:8025`); production uses Gmail SMTP with an App Password supplied through the notification service's `EmailSettings`.
- **EF Core Outbox pattern in Order and Payments** — integration events are written atomically to the same PostgreSQL transaction as the business data, guaranteeing at-least-once delivery and preventing dual-write inconsistencies.
- **JWT authentication via Keycloak, validated at Gateway and per-service** — Ocelot validates the Bearer token at the gateway edge; each downstream service independently re-validates via the Keycloak OIDC discovery endpoint, so a compromised gateway cannot bypass service-level auth.
- **Polly v8 resilience (retry + circuit breaker) on all outbound calls** — `AddHttpResilienceWithCircuitBreaker()`, `AddRedisResilience()`, and `AddNpgsqlResilience()` from AK.BuildingBlocks wrap every external dependency with exponential backoff retry and a half-open circuit breaker.
- **Serilog → Elasticsearch → Kibana for structured observability** — every service ships structured JSON logs with a `X-Correlation-Id` header propagated end-to-end; Kibana dashboards provide cross-service request tracing without a separate APM agent.

### Architecture Decision Records

Key design and platform decisions are captured as ADRs in [docs/adr/](docs/adr/).

| ADR | Decision | Summary |
|-----|------------------------------------------------|---------|
| [ADR-001](docs/adr/ADR-001-microservices-architecture.md) | Microservices&nbsp;Architecture | Independently deployable .NET 9 services over a monolith; each owns its data and deployment lifecycle. |
| [ADR-002](docs/adr/ADR-002-clean-architecture-and-ddd.md) | Clean&nbsp;Architecture&nbsp;&&nbsp;DDD | Domain / Application / Infrastructure / API layering with inward dependencies and a rich domain model. |
| [ADR-003](docs/adr/ADR-003-fault-tolerance-with-polly.md) | Fault&nbsp;Tolerance&nbsp;with&nbsp;Polly | Retry + circuit breaker + timeout pipelines (Polly v8) on all outbound calls, with graceful degradation. |
| [ADR-004](docs/adr/ADR-004-polyglot-persistence.md) | Polyglot&nbsp;Persistence | One database per service, each chosen to fit its workload (MongoDB, PostgreSQL, Redis, SQLite). |
| [ADR-005](docs/adr/ADR-005-saga-orchestration.md) | SAGA&nbsp;Orchestration | Orchestrated SAGA (MassTransit state machine) over 2PC and pure choreography for the order workflow. |
| [ADR-006](docs/adr/ADR-006-ocelot-api-gateway.md) | Ocelot&nbsp;API&nbsp;Gateway | Ocelot as the in-process gateway (routing, JWT, rate limiting, QoS) over YARP. |
| [ADR-007](docs/adr/ADR-007-masstransit-over-raw-rabbitmq.md) | MassTransit&nbsp;over&nbsp;Raw&nbsp;RabbitMQ | MassTransit for SAGA, outbox, retry, and consumer pipelines instead of the raw RabbitMQ client. |
| [ADR-008](docs/adr/ADR-008-shared-ddd-contracts-in-buildingblocks.md) | Shared&nbsp;DDD&nbsp;Contracts | Common DDD base types and contracts centralised in AK.BuildingBlocks. |
| [ADR-009](docs/adr/ADR-009-domain-events-vs-integration-events.md) | Domain&nbsp;vs&nbsp;Integration&nbsp;Events | Two distinct event patterns — in-process domain events vs cross-service integration events. |
| [ADR-010](docs/adr/ADR-010-CQRS-and-MediatR.md) | CQRS&nbsp;and&nbsp;MediatR | Command/query separation via MediatR with a validation pipeline behavior. |
| [ADR-011](docs/adr/ADR-011-Repository-Specification-and-Unit-of-Work.md) | Repository,&nbsp;Specification&nbsp;&&nbsp;UoW | Repository, Specification, and Unit of Work patterns for persistence abstraction. |
| [ADR-012](docs/adr/ADR-012-iac-with-terraform-terragrunt.md) | IaC&nbsp;with&nbsp;Terraform&nbsp;&&nbsp;Terragrunt | All Azure infrastructure as code, composed and kept DRY with Terragrunt. |
| [ADR-013](docs/adr/ADR-013-key-vault-rbac-and-observability-foundation.md) | Key&nbsp;Vault&nbsp;RBAC&nbsp;&&nbsp;Observability | Key Vault RBAC authorization, workspace-based App Insights, and an ACR Basic→Premium strategy. |
| [ADR-014](docs/adr/ADR-014-cosmosdb-and-servicebus.md) | Cosmos&nbsp;DB&nbsp;&&nbsp;Service&nbsp;Bus | Cosmos DB (Mongo API, serverless) and Azure Service Bus (Standard) as managed cloud backbones. |
| [ADR-015](docs/adr/ADR-015-messaging-migration-to-service-bus.md) | Messaging&nbsp;→&nbsp;Service&nbsp;Bus | Migrate messaging from RabbitMQ to Azure Service Bus with token (managed identity) auth. |
| [ADR-016](docs/adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) | Cosmos&nbsp;Data&nbsp;Migration&nbsp;&&nbsp;Workload&nbsp;Identity | Move Products persistence to Cosmos DB and establish the Workload Identity foundation. |
| [ADR-017](docs/adr/ADR-017-entra-id-functions-eventgrid.md) | Entra&nbsp;ID,&nbsp;Functions&nbsp;&&nbsp;Event&nbsp;Grid | Replace Keycloak with Entra ID; isolated-worker Azure Functions and Event Grid routing. |
| [ADR-018](docs/adr/ADR-018-aks-workload-identity-base-image.md) | AKS&nbsp;&&nbsp;Hardened&nbsp;Base&nbsp;Image | AKS cluster with Workload Identity and a custom hardened .NET base image. |
| [ADR-019](docs/adr/ADR-019-serverless-notification-functions-eventgrid.md) | Serverless&nbsp;Notification | Notification as Consumption-plan Azure Functions; Service Bus vs Event Grid transport boundary. |
| [ADR-020](docs/adr/ADR-020-api-management-managed-edge-gateway.md) | API&nbsp;Management&nbsp;Edge&nbsp;Gateway | Azure API Management as the managed external edge in front of internal cluster routing. |

---

## Solution Structure

```
AntKart/
├── AK.Products/          REST Minimal API — product catalogue (MongoDB)
├── AK.Discount/          gRPC service — discount coupons (SQLite)
├── AK.ShoppingCart/      REST Minimal API — shopping cart (Redis)
├── AK.Order/             REST Minimal API — order management (PostgreSQL + SAGA)
├── AK.UserIdentity/      REST Minimal API — Keycloak identity proxy
├── AK.Gateway/           API Gateway — Ocelot single entry point
├── AK.Payments/          REST Minimal API — payment processing (PostgreSQL + Razorpay)
├── AK.Notification/      REST Minimal API — transactional notifications (PostgreSQL + Mailhog/SMTP)
├── AK.BuildingBlocks/    Shared library (messaging, resilience, logging, auth)
├── AK.IntegrationTests/  SAGA + event bus + notification consumer tests (MassTransit in-memory harness)
├── AntKart.postman_collection.json
├── docs/
│   ├── adr/              Architecture Decision Records
│   ├── architecture/     C4 model diagrams (C4Architecture.md + Structurizr workspace)
│   ├── design/           Cross-cutting design docs (EVENTBUS, RESILIENCE, OBSERVABILITY)
│   ├── skills/           Step-by-step development & maintenance guides (12 skills)
│   └── test/             Manual test & security test guides (DevTestGuide, SECURITY_TESTS)
└── nuget.config
```

---

## Microservices

| Service | Transport | Database | Port (Docker) | Design Doc |
|---------|-----------|----------|---------------|------------|
| [AK.Products](AK.Products/AK.Products.API) | REST Minimal API | MongoDB | 8080 | [Products Design](AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) |
| [AK.Discount](AK.Discount/AK.Discount.Grpc) | gRPC | SQLite | 8081 | [Discount Design](AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) |
| [AK.ShoppingCart](AK.ShoppingCart/AK.ShoppingCart.API) | REST Minimal API | Redis | 8082 | [ShoppingCart Design](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) |
| [AK.Order](AK.Order/AK.Order.API) | REST Minimal API | PostgreSQL | 8083 | [Order Design](AK.Order/ORDER_TECHNICAL_DESIGN.md) |
| [AK.UserIdentity](AK.UserIdentity/AK.UserIdentity.API) | REST Minimal API | Keycloak | 8084 | [Identity Design](AK.UserIdentity/IDENTITY_TECHNICAL_DESIGN.md) |
| [AK.Payments](AK.Payments/AK.Payments.API) | REST Minimal API | PostgreSQL + Razorpay | 8085 | [Payments Design](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) |
| [AK.Notification](AK.Notification/AK.Notification.API) | REST Minimal API | PostgreSQL + Mailhog/SMTP | 8086 | [Notification Design](AK.Notification/NOTIFICATION_TECHNICAL_DESIGN.md) |
| [AK.Gateway](AK.Gateway/AK.Gateway.API) | Ocelot API Gateway | — | 9090 | [Gateway Design](AK.Gateway/API_GATEWAY.md) |

## Cross-Cutting

| Component | Technology | Design Doc |
|-----------|-----------|------------|
| BuildingBlocks | Shared DDD base classes, auth, messaging, resilience, middleware | [BUILDING_BLOCKS.md](AK.BuildingBlocks/BUILDING_BLOCKS.md) |
| Event Bus | MassTransit + RabbitMQ + SAGA + Outbox | [EVENTBUS.md](docs/design/EVENTBUS.md) |
| Resilience | Polly v8 (retry, circuit breaker, timeout) | [RESILIENCE.md](docs/design/RESILIENCE.md) |
| Observability | Serilog + Elasticsearch + Kibana | [OBSERVABILITY.md](docs/design/OBSERVABILITY.md) |
| Integration Tests | MassTransit in-memory test harness | [INTEGRATION_TESTS.md](AK.IntegrationTests/INTEGRATION_TESTS.md) |
| Architecture Decisions | Why each key technology was chosen | [docs/adr/](docs/adr/) |
| C4 Architecture | System context, containers, components, and code diagrams | [C4Architecture.md](docs/architecture/C4Architecture.md) |
| Security Tests | Ethical black-box & grey-box security test guide (15 categories) | [SECURITY_TESTS.md](docs/test/SECURITY_TESTS.md) |
| Skills | Step-by-step guides for development, maintenance, and verification tasks | [docs/skills/](docs/skills/) |
| Developer Testing Guide | Fresher-level end-to-end manual test guide (Postman, RabbitMQ, Kibana, SAGA, payments) | [DevTestGuide.md](docs/test/DevTestGuide.md) |

---

## Authorization

| Service | GET / Read | Write / Mutation |
|---------|-----------|-----------------|
| AK.Products | Anonymous | Admin only |
| AK.Discount (gRPC) | Anonymous | Admin only (JWT in `authorization` metadata) |
| AK.ShoppingCart | Authenticated | Authenticated |
| AK.Order | Authenticated (`/me` = own orders) | Authenticated; status update = Admin only |
| AK.Payments | Authenticated (`/me` = own payments) | Authenticated |
| AK.Notification | Authenticated (`/` = own notifications; `/admin` = Admin only) | Event-driven only — no write endpoints |
| AK.UserIdentity | `/login`, `/register`, `/refresh` anonymous | `/me` authenticated; `/admin/*` admin only |
| AK.Gateway | Proxied from downstream | JWT validated at gateway + downstream |

**Roles:** `user` (standard), `admin` (full access)
**Token issuer:** Keycloak realm `antkart` — get a token via `POST /api/auth/login`

---

## Running the Full Stack

This repository targets **cloud deployment**. There is no local docker-compose stack — run the services locally against live cloud services (databases, message broker, identity) or debug them via **cloud port-forwarding**. The earlier docker-compose-based Phase-1 local stack is preserved in the public AntKart reference repository.

The endpoints below use the illustrative ports from that reference setup:

| Service | URL |
|---------|-----|
| **API Gateway** | http://localhost:9090 |
| Keycloak Admin | http://localhost:8090 |
| RabbitMQ Management | http://localhost:15672  (guest/guest) |
| Kibana | http://localhost:5601 |
| AK.Products Swagger | http://localhost:8080/swagger (Development only) |
| AK.Discount gRPC | http://localhost:8081 |
| AK.ShoppingCart Swagger | http://localhost:8082/swagger (Development only) |
| AK.Order Swagger | http://localhost:8083/swagger (Development only) |
| AK.UserIdentity Swagger | http://localhost:8084/swagger (Development only) |
| AK.Payments Swagger | http://localhost:8085/swagger (Development only) |
| AK.Notification Swagger | http://localhost:8086/swagger (Development only) |
| **Mailhog Web UI** | **http://localhost:8025** (captured emails) |

> **Keycloak auto-import:** The `antkart` realm is imported from `keycloak/antkart-realm.json` on first start. Pre-seeded users: `admin/admin123` (admin+user), `user1/user123` (user), `admin2/Admin2Pass!` (admin+user).

### Individual services (dev)

```bash
# With the backing services (Keycloak, RabbitMQ, MongoDB, Redis, PostgreSQL,
# Elasticsearch) reachable in the cloud — directly or via port-forward —
# run each service locally in separate terminals:
cd AK.Products/AK.Products.API && dotnet run    # :5077
cd AK.Discount/AK.Discount.Grpc && dotnet run   # :5001
cd AK.ShoppingCart/AK.ShoppingCart.API && dotnet run  # :5079
cd AK.Order/AK.Order.API && dotnet run          # :5080
cd AK.UserIdentity/AK.UserIdentity.API && dotnet run  # :5085
cd AK.Payments/AK.Payments.API && dotnet run          # :5086
cd AK.Notification/AK.Notification.API && dotnet run  # :5087
cd AK.Gateway/AK.Gateway.API && dotnet run            # :8000
```

---

## API Testing

Import **[AntKart.postman_collection.json](AntKart.postman_collection.json)** into Postman.

| Variable | Default | Description |
|----------|---------|-------------|
| `gatewayUrl` | `http://localhost:9090` | API Gateway (recommended entry point) |
| `productsUrl` | `http://localhost:8080` | Products direct |
| `cartUrl` | `http://localhost:8082` | ShoppingCart direct |
| `orderUrl` | `http://localhost:8083` | Order direct |
| `identityUrl` | `http://localhost:8084` | UserIdentity direct |
| `accessToken` | (set after login) | JWT Bearer token |

---

## Tests

```bash
dotnet test
```

| Project | Tests |
|---------|-------|
| AK.Products.Tests | 202 |
| AK.Discount.Tests | 53 |
| AK.ShoppingCart.Tests | 88 |
| AK.Order.Tests | 113 |
| AK.UserIdentity.Tests | 20 |
| AK.IntegrationTests | 35 |
| AK.Payments.Tests | 70 |
| AK.Notification.Tests | 37 |
| **Total** | **618** |
