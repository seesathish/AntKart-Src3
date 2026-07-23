# Testing

This is the single entry point for AntKart's verification strategy. It indexes every test type, from code-level checks to end-to-end and security validation, so a reviewer can assess coverage from one place.

The platform is verified at every layer. **Unit tests** confirm domain logic, validators, and handlers in isolation. **Integration tests** verify the orchestrated SAGA and event-bus flows on an in-memory transport. **End-to-end tests** exercise the running services through their public surface. **Security tests** probe authentication, authorization, and input handling. **Load and performance tests** confirm behaviour under high-volume transaction throughput against cloud services. The unit and integration suites are layer-agnostic — they run identically regardless of where the services are deployed — while the end-to-end, security, and performance tests run against running services and grow with the deployment topology.

---

## Unit Tests

Per-project automated unit tests covering domain logic, validators, command and query handlers, mappers, and consumers — written with **xUnit**, **Moq**, and **FluentAssertions**. They have no external dependencies (no database, broker, or network) and run on any machine or pipeline.

```bash
dotnet test
```

| Project | Tests |
|---------|-------|
| AK.Products.Tests | 218 |
| AK.Order.Tests | 133 |
| AK.ShoppingCart.Tests | 88 |
| AK.Payments.Tests | 73 |
| AK.Discount.Tests | 49 |
| AK.Notification.Tests | 23 |
| AK.BuildingBlocks.Tests | 6 |
| AK.Tools.ProductsSeedLoader.Tests | 11 |
| AK.Tools.DiscountSeedLoader.Tests | 12 |
| **Unit subtotal** | **613** |

With the **28** integration tests below, the full automated suite is **641 tests** (all passing).

---

## Integration Tests

The `AK.IntegrationTests` suite (**28 tests**) exercises the orchestrated SAGA, event-bus flows, and payment event routing using **MassTransit's in-memory test harness** — no broker, no database, and no running host. It validates the messaging contracts and orchestration logic deterministically and in isolation.

Detail: [AK.IntegrationTests/INTEGRATION_TESTS.md](../../AK.IntegrationTests/INTEGRATION_TESTS.md).

---

## End-to-End / Functional Tests

The [Developer Testing Guide](DevTestGuide.md) walks every service end-to-end through its public surface — positive flows, negative flows, SAGA compensation scenarios, event-flow monitoring, log/correlation tracing, and notification delivery.

To call the APIs you need a token. For the interactive sign-in that obtains a delegated user token from Entra ID via Postman (OAuth2 Authorization Code + PKCE), and the most common pitfalls (the audience claim and 401s), see [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md).

- **Local-to-code verification (available now).** The guide validates the codebase end-to-end against its backing services.
- **Full-cloud verification via the ingress (available now).** The platform is verified through the public HTTPS ingress — see the section below. Verification through **Azure API Management** follows once the managed edge is in place.

---

## Cluster End-to-End Verification (public ingress)

The platform is verified against the **cluster** through its public HTTPS entry point — the ingress in front of the gateway — using a Postman collection that targets the **gateway routes** (not the individual services, which are internal `ClusterIP`). The base URL is the ingress hostname, `https://<public-ip>.nip.io` (a Let's Encrypt certificate terminates TLS; on the staging issuer the certificate is untrusted, so disable TLS verification in Postman or import the staging root).

The verified journey:

1. **Health** — `GET /gateway/health/{products|cart|orders|payments}` returns 200 for each backing service; the gateway's own `GET /health/live` and `/health/ready` return 200.
2. **Browse** — `GET /gateway/products` (and `/gateway/products/{id}`) returns the catalogue.
3. **Add to cart** — `POST /gateway/cart/items`, then `GET /gateway/cart` returns the current user's cart.
4. **Create order** — `POST /gateway/orders` drives server-authoritative price revalidation, the orchestrated SAGA (stock reservation → order confirmation), cart clearing, and both notification emails delivered via **Event Grid → Functions → ACS**.

Calls need a delegated Entra token in the `Authorization: Bearer` header (see [OAuth2 Authorization Code + PKCE Concepts](../guides/oauth2-pkce-concepts.md)).

**Pricing is server-authoritative.** The order is always priced from the catalogue. A submitted line price **below** the catalogue price is not honoured as a discount — because the catalogue price is higher, it is treated as a **price increase** and the order is rejected with `409 PriceChanged` (equal or a submitted price above the catalogue is accepted and charged the catalogue price; a missing/inactive product returns `422`; an unreachable catalogue fails closed with `503`).

### Gateway route mapping

External clients call the **gateway upstream** path; the gateway rewrites it to the service's own `/api` path over cluster DNS. Discount is internal-only (gRPC, called by Products) and has **no** gateway route.

| Service | Service API path (internal) | Gateway upstream path (external) |
|---------|-----------------------------|----------------------------------|
| Products | `GET/POST /api/v1/products`, `.../api/v1/products/{id\|categories\|featured\|category/{c}}` | `/gateway/products`, `/gateway/products/{everything}` |
| ShoppingCart | `GET/DELETE /api/v1/cart`, `POST/PUT/DELETE /api/v1/cart/items/...` | `/gateway/cart`, `/gateway/cart/{everything}` |
| Order | `GET/POST /api/orders`, `.../api/orders/{me\|{id}\|{id}/status}` | `/gateway/orders`, `/gateway/orders/{everything}` |
| Payments | `/api/payments/{initiate\|verify\|me\|{id}\|order/{id}}`, `/api/payments/cards...` | `/gateway/payments/{everything}` |
| Per-service health | `GET /health` | `GET /gateway/health/{products\|cart\|orders\|payments}` |
| Gateway (own) | `GET /health/live`, `/health/ready` | served directly at the ingress root (`/health/*`) |

Both a **bare** route (`/gateway/products`, `/gateway/cart`, `/gateway/orders`) and an `{everything}` route exist wherever a service exposes an endpoint with no trailing segment (e.g. `GET /api/v1/cart`); Payments has no bare `/api/payments` endpoint, so only its `{everything}` route is defined.

---

## Security Tests

The [Security Test Guide](SECURITY_TESTS.md) covers ethical black-box and grey-box security testing — authentication and authorization boundaries, ownership enforcement, input validation, and token handling — run against the live, running services.

---

## Load / Performance Tests

High-volume transaction testing will validate throughput, latency, and resilience under sustained load against the managed cloud services (data stores, messaging, and the gateway), confirming the platform's scaling and circuit-breaking behaviour.

**(To be added)** — the load and performance test guide follows the cloud deployment and Kubernetes phases, once the managed services and ingress are in place.
