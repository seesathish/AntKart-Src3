# AntKart — C4 Architecture

The [C4 model](https://c4model.com/) describes software architecture at four levels of zoom. Each level answers a different question for a different audience.

> **Source of truth:** These diagrams are generated from [`docs/architecture/workspace.dsl`](docs/architecture/workspace.dsl) using [Structurizr](https://structurizr.com), the reference C4 tooling by Simon Brown. Edit the DSL, re-export, and all diagrams stay consistent.

| Level | Diagram | Question |
|-------|---------|----------|
| 1 | System Context | Who uses AntKart and what external systems does it depend on? |
| 2 | Container | What are the deployable units and how do they communicate? |
| 3 | Component | How is AK.Order structured internally? |
| — | Dynamic | What happens when a customer places an order? |

---

## Level 1 — System Context

AntKart as a single system, showing the two actors (Customer, Administrator) and five external dependencies: Keycloak for identity, Razorpay for payments, SMTP for email, RabbitMQ for messaging, and ELK for observability.

![Level 1 — System Context](docs/architecture/c4-level1-system-context.png)

![Legend](docs/architecture/c4-legend.png)

---

## Level 2 — Container

Eight independently deployable microservices behind an Ocelot API Gateway. Each service owns its database: MongoDB (Products), PostgreSQL (Orders, Payments, Notifications), Redis (Cart), SQLite (Discount). Services communicate asynchronously via RabbitMQ with MassTransit, except AK.Discount which is called synchronously via gRPC.

![Level 2 — Container](docs/architecture/c4-level2-container.png)

---

## Level 3 — Component: AK.Order

AK.Order is the most architecturally rich service — CQRS via MediatR, SAGA orchestration with MassTransit, EF Core Outbox for guaranteed event delivery, and a domain model with an enforced state machine. Commands flow through a ValidationBehavior pipeline. CancelOrder and UpdateOrderStatus return `Result<T>` for expected failures; CreateOrder uses exceptions for unexpected ones.

![Level 3 — Component: AK.Order](docs/architecture/c4-level3-order-components.png)

> For detailed component descriptions, see [ORDER_TECHNICAL_DESIGN.md](AK.Order/ORDER_TECHNICAL_DESIGN.md).

---

## Order Flow — Happy Path

The end-to-end flow for placing an order: Customer → Gateway → Order (creates via Outbox) → RabbitMQ → Products (reserves stock) → SAGA confirms → Payment initiated → Razorpay verifies → Payment succeeded → Order updated to Paid → Notification emails sent at each stage.

![Order Flow — Dynamic View](docs/architecture/c4-order-flow-dynamic.png)

---

## Per-Service Documentation

Each service has a dedicated technical design document with internal architecture details, domain models, and API contracts.

| Service | Design Document |
|---------|----------------|
| AK.Products | [PRODUCTS_TECHNICAL_DESIGN.md](AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) |
| AK.Order | [ORDER_TECHNICAL_DESIGN.md](AK.Order/ORDER_TECHNICAL_DESIGN.md) |
| AK.Payments | [PAYMENTS_TECHNICAL_DESIGN.md](AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) |
| AK.ShoppingCart | [SHOPPING_CART_TECHNICAL_DESIGN.md](AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) |
| AK.Notification | [NOTIFICATION_TECHNICAL_DESIGN.md](AK.Notification/NOTIFICATION_TECHNICAL_DESIGN.md) |
| AK.Discount | [DISCOUNT_TECHNICAL_DESIGN.md](AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) |
| AK.UserIdentity | [IDENTITY_TECHNICAL_DESIGN.md](AK.UserIdentity/IDENTITY_TECHNICAL_DESIGN.md) |
| AK.Gateway | [API_GATEWAY.md](AK.Gateway/API_GATEWAY.md) |

Architecture Decision Records: [docs/adr/](docs/adr/)
