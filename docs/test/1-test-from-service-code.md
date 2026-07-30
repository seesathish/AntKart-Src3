# Testing from the service code — data, security, functional

This is the developer-facing verification path: what you run **from the code and from your machine** to check the platform against **live cloud resources**. It has three facets — the **data** the services persist, the **security** boundaries they enforce, and the **functional** behaviour of their endpoints — sitting on top of the layer-agnostic automated suites that run in CI.

> **Only cloud counts.** The delivered platform is verified against cloud resources through the public HTTPS endpoint **`https://api.antkart.in`**. A `localhost`/Docker-Compose run does not exercise Entra ID, Service Bus, Cosmos DB, ACS, or the AKS ingress, and its results are not valid. The [Phase-1 Developer Manual Test Guide](DevTestGuide.md) and its localhost URLs are retained for reference only.

## Automated suites (the code-level baseline)

The unit and in-memory integration suites are **layer-agnostic** — they run identically anywhere and gate every pull request in CI.

```bash
dotnet test
```

- **Unit tests** — domain logic, validators, command/query handlers, mappers, and consumers, with xUnit + Moq + FluentAssertions and no external dependency.
- **Integration tests** — the orchestrated SAGA, event-bus flows, and payment routing on MassTransit's in-memory harness (no broker, no DB, no host).
- Full counts, per-project breakdown, and the SAGA/event-bus detail live in the [Testing index](README.md) and [AK.IntegrationTests](../../AK.IntegrationTests/INTEGRATION_TESTS.md).

## Getting a token (prerequisite for the cloud facets)

Every protected call needs a delegated Entra token. Acquire one via the OAuth2 Authorization Code + PKCE flow — the interactive sign-in, the audience-claim pitfalls, and the common 401 causes are in [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md). Provision test users and assign app roles in Entra / Microsoft Graph — there is no application registration endpoint.

## Functional — endpoints against the cloud

Drive each service through the **gateway** (`https://api.antkart.in/gateway/*`) with the Postman collection or `curl`, covering positive and negative flows: browse the catalogue, add to cart, create an order (which drives server-authoritative price revalidation and the SAGA), initiate and verify a payment, and confirm notification emails arrive. The gateway route mapping (external `/gateway/*` → internal `/api/*`) is tabulated in the [Testing index](README.md#cluster-end-to-end-verification-public-ingress). The full step-by-step journey is [Full-cloud end-to-end](2-full-cloud-end-to-end.md).

## Security — auth, ownership, input, exposure

The [Security Test Guide](SECURITY_TESTS.md) is the authoritative set of black-box/grey-box checks run against the live services:

- **Authentication** — unauthenticated routes return 401; tampered and `alg:none` tokens are rejected at the gateway and again at each service.
- **Authorization** — a non-admin is refused admin routes (order-status update, product writes) with 403.
- **Ownership / IDOR** — a user can never read or mutate another user's order, payment, or cart; identity comes from the JWT `sub`, never a path or body field.
- **Input & mass assignment** — invalid amounts return 400; injected `userId`/`isAdmin`/`role` fields are dropped.
- **Exposure** — no stack traces in error bodies, no `Server` header, per-route rate limiting returns 429.

## Data — what the services persist

Verify the persisted state behind the endpoints against the managed data stores:

- **Products** — the catalogue is served from Cosmos DB (MongoDB API); confirm reads return seeded products and that seeding is idempotent (see the [Products design](../../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md)).
- **Order / Payments / Discount** — PostgreSQL Flexible Server holds orders, payments, and coupons; confirm an order's status transitions and a payment's outcome are persisted after the SAGA settles.
- **Cart** — Azure Managed Redis holds the per-user cart under `AKCart:cart:{userId}` with a 30-day TTL.
- **Notification history** — each dispatch writes an audit row via the serverless Core (see [Serverless & Eventing Concepts](../guides/serverless-eventing-concepts.md)).

Reaching a store directly is a `kubectl port-forward` (cluster) or a run against the live service; the connection is always secret-less via workload identity — see [Kubernetes](../development/3-kubernetes.md) and [Security](../development/6-security.md).

## Related

- The end-to-end journey through the ingress: [Full-cloud end-to-end](2-full-cloud-end-to-end.md).
- The full verification strategy and counts: [Testing index](README.md).
- Load / performance testing is planned — see the [Roadmap](../ROADMAP.md).
