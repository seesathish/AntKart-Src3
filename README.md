# AntKart

AntKart is a cloud-native e-commerce platform implemented as eight independently deployable .NET 9 microservices. It is engineered as a reference implementation: each service applies Clean Architecture and Domain-Driven Design, services coordinate through an event-driven SAGA rather than synchronous service-to-service calls, and the platform is provisioned for the cloud entirely as code.

The system is defined at two layers. The **application baseline** runs the services against self-hosted backing infrastructure — Keycloak, RabbitMQ, per-service databases, and ELK — and builds and runs locally. The **cloud-native target** maps the same services onto managed Azure services — Microsoft Entra ID, Azure Service Bus, and Azure Cosmos DB — under a secret-less, identity-based security posture. Application code is largely identical across both layers; only the infrastructure it binds to changes.

The codebase is accompanied by architecture decision records, concept primers, and step-by-step build guides that record the rationale behind each design and infrastructure decision.

---

## Architecture

The architecture is modelled with the [C4 model](https://c4model.com) and rendered from a single Structurizr DSL workspace. The images below are generated artifacts: [`docs/architecture/workspace.dsl`](docs/architecture/workspace.dsl) is the single source of truth, and [`docs/architecture/C4Architecture.md`](docs/architecture/C4Architecture.md) is the detailed reference.

> **Note:** The rendered diagrams reflect the **pre-migration topology** (including the now-retired application identity service and the former identity provider) and are **(to be updated post-migration)**, once the migration round is complete. The service catalogue and structure below reflect the current state.

### Level 1 — System Context

![C4 Level 1 — System Context](docs/architecture/c4-level1-system-context.png)

AntKart as a single system with two actors — Customer and Administrator — and its external dependencies for identity, payments, email, messaging, and observability. Establishes the system boundary and the integrations that cross it.

### Level 2 — Container

![C4 Level 2 — Container](docs/architecture/c4-level2-container.png)

The microservices behind the Ocelot API gateway, each owning its own data store. Inter-service communication is asynchronous over the message broker via MassTransit; AK.Discount is the single synchronous gRPC dependency, invoked by AK.Order.

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
| Relational / cache | PostgreSQL · Redis | Managed Azure data services |
| Email | MailKit · SMTP / Mailhog | SMTP / managed email |
| Payments | Razorpay (sandbox) | Razorpay |
| Resilience | Polly v8 (retry · circuit breaker · timeout) | Unchanged |
| Infrastructure as code | — | Terraform + Terragrunt |
| Container / orchestration | Docker | Azure Kubernetes Service + Azure Container Registry (target) |
| Serverless / eventing | — | Azure Functions + Event Grid (target) |
| Secrets / access | Connection strings | Key Vault + managed identities (no secrets) |
| Observability | Serilog → Elasticsearch → Kibana | Azure Monitor / Application Insights |
| Testing | xUnit · Moq · FluentAssertions (631 tests) | Unchanged |

---

## Documentation

| Document | Scope |
|----------|-------|
| [Development Guide](DevelopmentGuide.md) | Master index of the build: each delivery phase with its build guide, prerequisite concepts, and governing ADRs. |
| [Architecture (C4)](docs/architecture/C4Architecture.md) | Detailed C4 model reference; diagrams generated from [`workspace.dsl`](docs/architecture/workspace.dsl). |
| [Infrastructure Guide](docs/guides/infrastructure-guide.md) | Step-by-step provisioning of the cloud platform — each resource as Understand → Build → Execute → Verify. |
| [infrastructure/README](infrastructure/README.md) | Layout and operating model of the Terraform/Terragrunt code. |
| Concept primers | First-principles references for the cloud-native domains: [IaC](docs/guides/iac-concepts.md), [Networking & Kubernetes](docs/guides/networking-concepts.md), [Identity](docs/guides/identity-concepts.md), [OAuth2 + PKCE](docs/guides/oauth2-pkce-concepts.md), [Messaging](docs/guides/messaging-concepts.md), [Serverless & Eventing](docs/guides/serverless-eventing-concepts.md), [Cosmos DB](docs/guides/cosmosdb-concepts.md). |
| Cross-cutting design notes | [Event Bus](docs/design/EVENTBUS.md), [Resilience](docs/design/RESILIENCE.md), and [Observability](docs/design/OBSERVABILITY.md) — the design of the messaging, resilience, and logging concerns. |
| [Building Blocks](AK.BuildingBlocks/BUILDING_BLOCKS.md) | The shared cross-cutting library: DDD base types, authentication, messaging, resilience, and middleware. |
| [Architecture Decision Records](docs/adr/README.md) | The rationale behind each significant design and infrastructure decision. |
| [Integration Tests](AK.IntegrationTests/INTEGRATION_TESTS.md) | SAGA and event-bus tests on the MassTransit in-memory harness. |
| [Developer Testing Guide](docs/test/DevTestGuide.md) | End-to-end manual verification across Postman, messaging, the SAGA, and payments. |
| [Security Test Guide](docs/test/SECURITY_TESTS.md) | Black-box and grey-box security testing methodology. |
| [Development & maintenance guides](docs/skills/) | Step-by-step procedures for common development and maintenance tasks. |

---

## Application Baseline and Cloud-Native Target

AntKart is defined at two layers. The **application baseline** — the eight services running against self-hosted backing infrastructure for identity, messaging, per-service databases, and logging — lives in the public AntKart (Phase 1) repository. This repository is the **cloud-native target**: the same services mapped onto managed Azure equivalents under a secret-less, identity-based posture, with the infrastructure defined as code. The application code is largely unchanged across the two; what differs is the infrastructure it binds to. The full concern-by-concern mapping is in the [Technology Stack](#technology-stack) above.

---

## Developer Challenge

AntKart is published as a professional reference — for architectural review and for hands-on reconstruction. It supports two complementary tracks.

**Study the architecture from the documentation and test artefacts.** Work through the cloud-native architecture, concepts, and implementation entirely from this repository's documentation and tests: the C4 model, the concept primers, the Architecture Decision Records, the Infrastructure Guide, and the integration and manual test suites. This track builds a complete understanding of how the platform is designed and verified without provisioning anything.

**Rebuild and validate the cloud-native platform.** Clone the public AntKart (Phase 1) microservices repository and undertake the cloud-native build and validation by following the [Development Guide](DevelopmentGuide.md) and the [Developer Testing Guide](docs/test/DevTestGuide.md): provision the infrastructure as code, map each baseline component to its managed equivalent, adopt the secret-less identity model, and verify each step end to end.

The relationship between the repositories is **Phase 1 (AntKart — microservices) → Phase 2 (AntKart-CloudNative — the cloud-native build derived from Phase 1)**.

---

## Solution Structure

```
AntKart/
├── AK.Products/          REST Minimal API — product catalogue (MongoDB / Cosmos DB)
├── AK.Discount/          gRPC service — discount coupons (PostgreSQL)
├── AK.ShoppingCart/      REST Minimal API — shopping cart (Redis)
├── AK.Order/             REST Minimal API — order management (PostgreSQL + SAGA)
├── AK.Gateway/           API Gateway — Ocelot single entry point
├── AK.Payments/          REST Minimal API — payment processing (PostgreSQL + Razorpay)
├── AK.Notification/      Serverless notifications — AK.Notification.Core (reusable library) + AK.Notification.Functions (.NET 9 isolated Azure Functions, Event Grid-triggered)
├── AK.BuildingBlocks/    Shared library (messaging, resilience, logging, auth)
├── AK.IntegrationTests/  SAGA + event bus tests (MassTransit in-memory harness)
├── AntKart.postman_collection.json
├── docs/
│   ├── adr/              Architecture Decision Records
│   ├── architecture/     C4 diagram images + Structurizr workspace
│   ├── design/           Cross-cutting design docs (EVENTBUS, RESILIENCE, OBSERVABILITY)
│   ├── guides/           Concept primers + the Infrastructure Guide
│   ├── skills/           Step-by-step development & maintenance guides
│   └── test/             Manual test & security test guides (DevTestGuide, SECURITY_TESTS)
├── infrastructure/       Terraform modules + Terragrunt live units
└── nuget.config
```

---

## Microservices

| Service | Transport | Data store | Design Doc |
|---------|-----------|------------|------------|
| [AK.Products](AK.Products/AK.Products.API) | REST Minimal API | MongoDB / Cosmos DB | [Products Design](AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) |
| [AK.Discount](AK.Discount/AK.Discount.Grpc) | gRPC | PostgreSQL | [Discount Design](AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) |
| [AK.ShoppingCart](AK.ShoppingCart/AK.ShoppingCart.API) | REST Minimal API | Redis | [ShoppingCart Design](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) |
| [AK.Order](AK.Order/AK.Order.API) | REST Minimal API | PostgreSQL | [Order Design](AK.Order/ORDER_TECHNICAL_DESIGN.md) |
| [AK.Payments](AK.Payments/AK.Payments.API) | REST Minimal API | PostgreSQL + Razorpay | [Payments Design](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) |
| [AK.Notification](AK.Notification/AK.Notification.Core) | Serverless (Event Grid + Azure Functions) | PostgreSQL (history) + ACS Email | [Cloud Migration Guide §7](docs/guides/cloud-migration-guide.md) |
| [AK.Gateway](AK.Gateway/AK.Gateway.API) | Ocelot API Gateway | — | [Gateway Design](AK.Gateway/API_GATEWAY.md) |

Cloud ingress and API Management endpoints for each service are **(to be updated)** as the deployment topology is finalized.

Identity is **Entra-native**: Microsoft Entra ID issues tokens and each service validates them directly, so there is no application identity service in the catalogue (see [ADR-021](docs/adr/ADR-021-retire-identity-service-for-entra.md)).

---

## Authorization

| Service | GET / Read | Write / Mutation |
|---------|-----------|-----------------|
| AK.Products | Anonymous | Admin only |
| AK.Discount (gRPC) | Anonymous | Admin only (JWT in `authorization` metadata) |
| AK.ShoppingCart | Authenticated | Authenticated |
| AK.Order | Authenticated (`/me` = own orders) | Authenticated; status update = Admin only |
| AK.Payments | Authenticated (`/me` = own payments) | Authenticated |
| AK.Notification.Functions | No HTTP surface (Event Grid-triggered) | Serverless side-effect — no client-facing endpoints |
| AK.Gateway | Proxied from downstream | JWT validated at gateway and downstream |

**Roles:** `user` (standard), `admin` (full access).

**Identity provider:** Microsoft Entra ID — **identity is Entra-native, with no application identity service**. Entra issues access tokens directly to clients via standard OAuth/OIDC flows; each service validates them (issuer, audience, lifetime, signature) and authorizes from the flat `roles` claim. User and app-role administration is performed in Entra / Microsoft Graph. Cloud endpoint and token-acquisition specifics are **(to be updated)** as the deployment topology is finalized.

---

## Getting Started

**Study the codebase.** Clone the public AntKart (Phase 1) microservices repository and validate the build and tests:

```bash
git clone https://github.com/seesathish/AntKart.git
cd AntKart
dotnet restore   # run from the repository root so nuget.config is applied
dotnet build
dotnet test      # 631 tests
```

**Provision the cloud-native platform.** Follow the [Infrastructure Guide](docs/guides/infrastructure-guide.md) to provision the managed Azure resources as code — Terraform modules and Terragrunt live units, with a reviewed `plan` before each `apply` — and the [Development Guide](DevelopmentGuide.md) for the delivery phases. With the managed services in place, each service runs locally against live cloud services, directly or via cloud port-forwarding. Service endpoints and ports are **(to be updated)** as the deployment topology is finalized.

---

## Testing

The platform is verified across unit, integration, end-to-end, security, and load/performance testing, backed by a comprehensive automated suite. The full strategy, the per-project breakdown, and links to each test type are consolidated in the [Testing index](docs/test/README.md).
