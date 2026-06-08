# AntKart

AntKart is a cloud-native e-commerce platform implemented as eight independently deployable .NET 9 microservices. It is engineered as a reference implementation: each service applies Clean Architecture and Domain-Driven Design, services coordinate through an event-driven SAGA rather than synchronous service-to-service calls, and the platform is provisioned for the cloud entirely as code.

The system is defined at two layers. The **application baseline** runs the services against self-hosted backing infrastructure — Keycloak, RabbitMQ, per-service databases, and ELK — and builds and runs locally. The **cloud-native target** maps the same services onto managed Azure services — Microsoft Entra ID, Azure Service Bus, and Azure Cosmos DB — under a secret-less, identity-based security posture. Application code is largely identical across both layers; only the infrastructure it binds to changes.

The codebase is accompanied by architecture decision records, concept primers, and step-by-step build guides that record the rationale behind each design and infrastructure decision.

---

## Architecture

The architecture is modelled with the [C4 model](https://c4model.com) and rendered from a single Structurizr DSL workspace. The images below are generated artifacts: [`docs/architecture/workspace.dsl`](docs/architecture/workspace.dsl) is the single source of truth, and [`docs/architecture/C4Architecture.md`](docs/architecture/C4Architecture.md) is the detailed reference.

### Level 1 — System Context

![C4 Level 1 — System Context](docs/architecture/c4-level1-system-context.png)

AntKart as a single system with two actors — Customer and Administrator — and its external dependencies for identity, payments, email, messaging, and observability. Establishes the system boundary and the integrations that cross it.

### Level 2 — Container

![C4 Level 2 — Container](docs/architecture/c4-level2-container.png)

The eight microservices behind the Ocelot API gateway, each owning its own data store. Inter-service communication is asynchronous over the message broker via MassTransit; AK.Discount is the single synchronous gRPC dependency, invoked by AK.Order.

### Level 3 — Component (AK.Order)

![C4 Level 3 — AK.Order Components](docs/architecture/c4-level3-order-components.png)

The internal structure of AK.Order, the most elaborate service: Minimal API endpoints, the MediatR command/query pipeline with a FluentValidation behaviour, the domain aggregate and its enforced state machine, and the EF Core repository and transactional outbox that feed the MassTransit saga.

### Dynamic View — Order Flow

![C4 Dynamic View — Order Flow](docs/architecture/c4-order-flow-dynamic.png)

The end-to-end order journey orchestrated by the saga: order creation with outbox publication, stock reservation, payment initiation and signature verification, and the status transitions and notifications emitted at each stage. No step is a direct service-to-service HTTP call.

### Cloud-Native Deployment Architecture

> **Forthcoming.** The managed-Azure deployment topology — AKS, managed data and messaging services, identity, and networking — will be added as a Structurizr-generated diagram once that part of the platform is delivered.

### CI/CD (DevOps) Architecture

> **Forthcoming.** The build, test, and release pipeline will be added as a Structurizr-generated diagram once the DevOps phase is delivered.

### Architecture Highlights

**Application-layer patterns**

- **Clean Architecture and DDD per service** — Domain, Application, Infrastructure, and API layers with strict inward dependency rules; domain entities expose private setters and factory methods and carry no framework concerns.
- **CQRS with MediatR** — commands and queries are fully separated, and a `ValidationBehavior<TRequest, TResponse>` pipeline validates every request through FluentValidation before it reaches a handler.
- **SAGA orchestration via MassTransit** — the `OrderSaga` in AK.Order transitions through `Initial → StockPending → Confirmed/Cancelled`, coordinating Products, ShoppingCart, Payments, and Notification over the broker with no direct service-to-service HTTP calls.
- **Transactional outbox** — in Order and Payments, integration events are written in the same database transaction as the business data, guaranteeing at-least-once delivery and eliminating dual-write inconsistency.
- **`Result<T>` error modelling** — expected outcomes (`CancelOrder`, `UpdateOrderStatus`) return `Result<T>`; genuinely exceptional paths (`CreateOrder`) use exceptions — a deliberate contrast documented in the design notes.
- **Polly-based resilience** — `AddHttpResilienceWithCircuitBreaker()`, `AddRedisResilience()`, and `AddNpgsqlResilience()` from AK.BuildingBlocks wrap every outbound dependency with exponential-backoff retry and a half-open circuit breaker.

**Cloud-native platform posture**

- **Infrastructure as code** — Terraform modules define resource shape; Terragrunt live units compose them per environment over a shared remote-state backend, with a reviewed `plan` preceding every `apply`.
- **Entra-only, secret-less data planes** — shared-key and local authentication are disabled (`local_auth_enabled = false`); data planes accept Microsoft Entra identities only, leaving no connection-string secrets in configuration.
- **Least-privilege RBAC with managed identities** — each service receives its own managed identity scoped to only the data-plane roles it requires; workload identity federation authenticates cluster workloads with no stored credential.
- **Centralized observability** — structured Serilog logs carry an end-to-end `X-Correlation-Id`, shipped to ELK in the baseline and to Azure Monitor / Application Insights in the cloud-native target.

Significant design and infrastructure decisions are recorded as [Architecture Decision Records](docs/adr/README.md).

---

## Technology Stack

| Concern | Application baseline | Cloud-native equivalent |
|---------|----------------------|-------------------------|
| Language / framework | .NET 9 · ASP.NET Core Minimal APIs · gRPC | Unchanged |
| Architecture & patterns | Clean Architecture · DDD · CQRS (MediatR 12) · SAGA · EF Core Outbox · `Result<T>` · FluentValidation | Unchanged |
| API gateway | Ocelot | Azure API Management (target) |
| Identity | Keycloak (OIDC / JWT) | Microsoft Entra ID |
| Messaging | RabbitMQ + MassTransit | Azure Service Bus + MassTransit |
| Product store | MongoDB | Azure Cosmos DB (MongoDB API) |
| Relational / cache | PostgreSQL · Redis · SQLite | Managed Azure data services |
| Email | MailKit · SMTP / Mailhog | SMTP / managed email |
| Payments | Razorpay (sandbox) | Razorpay |
| Resilience | Polly v8 (retry · circuit breaker · timeout) | Unchanged |
| Infrastructure as code | — | Terraform + Terragrunt |
| Container / orchestration | Docker | Azure Kubernetes Service + Azure Container Registry (target) |
| Serverless / eventing | — | Azure Functions + Event Grid (target) |
| Secrets / access | Connection strings | Key Vault + managed identities (no secrets) |
| Observability | Serilog → Elasticsearch → Kibana | Azure Monitor / Application Insights |
| Testing | xUnit · Moq · FluentAssertions (618 tests) | Unchanged |

---

## Quick Start

Prerequisites: the [.NET 9 SDK](https://dotnet.microsoft.com/download), and Docker for the backing services (databases, message broker, identity provider, mail trap).

Build and test:

```bash
git clone https://github.com/seesathish/AntKart-Src3.git
cd AntKart-Src3
dotnet restore   # run from the repository root so nuget.config is applied
dotnet build
dotnet test      # 618 tests
```

Run a service:

```bash
cd AK.Products/AK.Products.API && dotnet run   # http://localhost:5077/swagger
```

Each service binds to its backing infrastructure. The complete self-hosted local stack — Docker Compose for Keycloak, RabbitMQ, the databases, Mailhog, and ELK — is preserved in the public AntKart reference repository; this repository targets cloud deployment, for which the [Infrastructure Guide](docs/guides/infrastructure-guide.md) is the provisioning reference. The [Postman collection](AntKart.postman_collection.json) and the [Developer Testing Guide](docs/test/DevTestGuide.md) cover end-to-end verification.

---

## Documentation

| Document | Scope |
|----------|-------|
| [Development Guide](DevelopmentGuide.md) | Master index of the build: each delivery phase with its build guide, prerequisite concepts, and governing ADRs. |
| [Architecture (C4)](docs/architecture/C4Architecture.md) | Detailed C4 model reference; diagrams generated from [`workspace.dsl`](docs/architecture/workspace.dsl). |
| [Infrastructure Guide](docs/guides/infrastructure-guide.md) | Step-by-step provisioning of the cloud platform — each resource as Understand → Build → Execute → Verify. |
| [infrastructure/README](infrastructure/README.md) | Layout and operating model of the Terraform/Terragrunt code. |
| Concept primers | First-principles references for the cloud-native domains: [IaC](docs/guides/iac-concepts.md), [Networking & Kubernetes](docs/guides/networking-concepts.md), [Identity](docs/guides/identity-concepts.md), [Messaging](docs/guides/messaging-concepts.md), [Serverless & Eventing](docs/guides/serverless-eventing-concepts.md), [Cosmos DB](docs/guides/cosmosdb-concepts.md). |
| [Architecture Decision Records](docs/adr/README.md) | The rationale behind each significant design and infrastructure decision. |
| [Developer Testing Guide](docs/test/DevTestGuide.md) | End-to-end manual verification across Postman, messaging, the SAGA, and payments. |
| [Security Test Guide](docs/test/SECURITY_TESTS.md) | Black-box and grey-box security testing methodology. |

---

## Application Baseline and Cloud-Native Target

AntKart is defined at two layers, and both are present throughout the documentation.

The **application baseline** is how the services are built and run against self-hosted backing infrastructure: Keycloak for identity, RabbitMQ for messaging, per-service local databases, and ELK for observability. It is the layer exercised by the [Quick Start](#quick-start).

The **cloud-native target** maps the same services onto managed Azure equivalents under a secret-less, identity-based posture — managed identities, least-privilege RBAC, and no connection strings. The application code is largely unchanged; the infrastructure it binds to is what differs.

The cloud-native platform does not replace the baseline; it maps it onto managed services:

| Application baseline | Cloud-native equivalent |
|----------------------|-------------------------|
| Keycloak (identity) | Microsoft Entra ID |
| RabbitMQ (messaging) | Azure Service Bus |
| MongoDB (product store) | Azure Cosmos DB (MongoDB API) |
| PostgreSQL / Redis | Managed Azure data services |
| ELK (Serilog → Elasticsearch → Kibana) | Azure Monitor / Application Insights |
| Connection-string secrets | Managed identities — no secrets |

---

## Learning Challenges

The repository supports two independent exercises.

**Cloud-native concepts from first principles.** The concept primers develop each cloud domain — IaC, networking and Kubernetes, identity, messaging, serverless and eventing, and Cosmos DB — without assuming prior cloud experience, and link to the ADR that applies each one. Begin with the [IaC primer](docs/guides/iac-concepts.md).

**Reconstruction of the cloud deployment.** Starting from the application baseline, the managed-Azure platform can be reproduced incrementally: provision the infrastructure as code, map each baseline component to its managed equivalent, and adopt the secret-less identity model. The [Infrastructure Guide](docs/guides/infrastructure-guide.md) sequences this resource by resource.

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
│   ├── architecture/     C4 diagram images + Structurizr workspace
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
