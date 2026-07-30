# Platform architecture — how the code is built

> **Diagrams pending review:** _Inside AK.Order_ and _Order saga_ are carried across as-is and will be reworked.

This layer is the engineering foundation: how a single AntKart service is structured and how the services coordinate. It is the same across all six services, so understanding one is understanding all.

Every service applies **Clean Architecture** and **Domain-Driven Design** — a `Domain` core with no outward dependencies, an `Application` layer of use cases (**CQRS** commands and queries dispatched by **MediatR** through a validation pipeline), an `Infrastructure` layer for persistence and messaging, and a thin **minimal-API** (or gRPC) host. Services never call each other synchronously for business flows; they coordinate through an **orchestrated saga** on Azure Service Bus, with a **transactional outbox** so a state change and its event are written atomically. Persistence hides behind **repository + specification + unit of work**.

AK.Order is the richest example — CQRS via MediatR, saga orchestration, EF Core outbox, and a domain state machine that enforces valid order transitions. Commands flow through a `ValidationBehavior` pipeline; `CancelOrder`/`UpdateOrderStatus` return `Result<T>` for expected failures while `CreateOrder` throws for the unexpected.

## Inside AK.Order

> **Diagram: Inside AK.Order** — _not yet drawn (C4 component view)_
> **Must show:** the internal structure of one service — API endpoints → MediatR (with the ValidationBehavior pipeline) → command/query handlers → domain aggregate with its state machine → repository/unit-of-work → EF Core DbContext and the outbox.

_C4 component view — will render from [`docs/C4Renders/`](../C4Renders/) (`workspace.dsl`)._

## Order saga

> **Diagram: Order saga** — _not yet drawn (C4 dynamic view)_
> **Must show:** the end-to-end order flow to a Paid state — Gateway → Order (created via the outbox) → stock reservation → saga confirm → payment initiated → Razorpay verified → payment succeeded → order updated to Paid, with a notification emitted at each stage.

_C4 dynamic view — will render from [`docs/C4Renders/`](../C4Renders/) (`workspace.dsl`)._

The two eventing patterns this rests on — **domain events** (in-process) vs **integration events** (cross-service, via the outbox and Service Bus) — and the resilience posture (retry, circuit breaker, timeout) are the platform's cross-cutting design.

## How it was built

- Application patterns and cross-cutting design: [Event Bus design](../guides/eventbus-concepts.md) · [Resilience design](../guides/resilience-concepts.md).
- Per-service internals: the service technical design documents — [AK.Products](../../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) · [AK.Order](../../AK.Order/ORDER_TECHNICAL_DESIGN.md) · [AK.Payments](../../AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) · [AK.ShoppingCart](../../AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) · [AK.Discount](../../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) · [AK.Gateway](../../AK.Gateway/API_GATEWAY.md) · [AK.BuildingBlocks](../../AK.BuildingBlocks/BUILDING_BLOCKS.md). _(These describe the Phase-1 topology and carry a superseded banner; the patterns still hold.)_

## Decisions

- [ADR-001 — Microservices Architecture](../adr/ADR-001-microservices-architecture.md)
- [ADR-002 — Clean Architecture and Domain-Driven Design](../adr/ADR-002-clean-architecture-and-ddd.md)
- [ADR-004 — Polyglot Persistence](../adr/ADR-004-polyglot-persistence.md)
- [ADR-005 — SAGA Orchestration over 2PC and Choreography](../adr/ADR-005-saga-orchestration.md)
- [ADR-006 — Ocelot API Gateway over YARP](../adr/ADR-006-ocelot-api-gateway.md)
- [ADR-009 — Domain Events vs Integration Events](../adr/ADR-009-domain-events-vs-integration-events.md)
- [ADR-010 — CQRS and MediatR](../adr/ADR-010-CQRS-and-MediatR.md)
- [ADR-011 — Repository, Specification, and Unit of Work](../adr/ADR-011-Repository-Specification-and-Unit-of-Work.md)

## Open items

- [KI-005 — no stock-release compensation on payment failure](../KNOWN_ISSUES.md): when a payment fails, stock reserved by the saga is retained rather than released — a deliberate deferral pending a compensation workstream.
