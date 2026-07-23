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
- **Full-cloud verification via ingress / API Management (to be added).** End-to-end validation through the cloud ingress and Azure API Management endpoints will be added after the Kubernetes phase, once the deployment topology is finalized.

---

## Security Tests

The [Security Test Guide](SECURITY_TESTS.md) covers ethical black-box and grey-box security testing — authentication and authorization boundaries, ownership enforcement, input validation, and token handling — run against the live, running services.

---

## Load / Performance Tests

High-volume transaction testing will validate throughput, latency, and resilience under sustained load against the managed cloud services (data stores, messaging, and the gateway), confirming the platform's scaling and circuit-breaking behaviour.

**(To be added)** — the load and performance test guide follows the cloud deployment and Kubernetes phases, once the managed services and ingress are in place.
