# Test Guide

How AntKart is verified — **cloud-only**. The delivered platform is validated only against the live cloud through the public HTTPS endpoint `https://api.antkart.in`; a localhost run does not exercise Entra ID, Service Bus, Cosmos DB, ACS, or the AKS ingress, so its results are not valid. The automated `dotnet test` unit + integration suites are the layer-agnostic code baseline that gates every pull request in CI.

- [Full-cloud end-to-end](docs/test/1-full-cloud-end-to-end.md) — the whole platform through the public ingress, driven by the `AntKart Cloud E2E Saga` Postman collection: the positive order path, the persisted-data checks, and the SAGA compensation path.
- [Security tests](docs/test/SECURITY_TESTS.md) — authentication, authorization, ownership, input, and exposure probes against the live services.
- [Testing index](docs/test/README.md) — the full verification strategy, the automated suite counts, and the gateway route mapping.
