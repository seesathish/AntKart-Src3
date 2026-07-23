# Development Guide

This guide is the entry point to how the AntKart platform is built and evolved through its cloud-native transformation. It gives the big picture and links out to focused deep-dive guides for each area.

It is a **map, not the detail** — each delivery phase below has a dedicated guide that captures the concepts, the scripts, the execution, and the verification for that area. For the reasoning behind the architecture, see the [Architecture Decision Records index](docs/adr/README.md); for the architecture overview and diagrams, see the [README](README.md#architecture-overview).

> **How to use this guide.** This is the **spine** of the documentation. Each delivery phase below links to three things: the **build guide(s)** that walk through doing it, the **concept guides** to read first to understand it, and the **ADRs** that record the key decisions. Each phase is marked with its **status** so you know what is complete and what is in progress.

---

## Delivery Phases

The platform is delivered as a series of phases. Each phase builds on the previous one and is documented in its own deep-dive guide.

### 1. Foundation · _Status: complete_

The clean application baseline: independently deployable .NET microservices built with Clean Architecture, Domain-Driven Design, CQRS, an event-driven SAGA, an API gateway, resilience, and structured observability. This phase establishes the architecture decisions and the repository structure that everything else builds on.

- **Build guides:** [Architecture overview](README.md#architecture-overview) · cross-cutting design docs — [Event Bus](docs/design/EVENTBUS.md), [Resilience](docs/design/RESILIENCE.md), [Observability](docs/design/OBSERVABILITY.md)
- **Concepts to read first:** the cross-cutting design docs above (the application's own patterns)
- **Decisions:** [ADR-001 through ADR-011](docs/adr/README.md) — microservices, Clean Architecture & DDD, fault tolerance, and the application patterns

### 2. Cloud Infrastructure (IaC) · _Status: complete (dev environment)_

All cloud resources are provisioned as code with Terraform and Terragrunt — networking, container registry, secrets, data stores, messaging, and supporting services — so the environment is reproducible, reviewable, and version-controlled.

- **Build guides:** [Infrastructure Guide](docs/guides/infrastructure-guide.md) (step-by-step) · [infrastructure/README](infrastructure/README.md) (the IaC map)
- **Concepts to read first:** [IaC fundamentals](docs/guides/iac-concepts.md) → [Networking & Kubernetes](docs/guides/networking-concepts.md) → [Identity](docs/guides/identity-concepts.md) (plus [Cosmos DB](docs/guides/cosmosdb-concepts.md), [Messaging](docs/guides/messaging-concepts.md), [Serverless & Eventing](docs/guides/serverless-eventing-concepts.md) for the resources it provisions)
- **Decisions:** [ADR-012](docs/adr/ADR-012-iac-with-terraform-terragrunt.md) (IaC with Terraform/Terragrunt) · [ADR-013](docs/adr/ADR-013-key-vault-rbac-and-observability-foundation.md) (Key Vault RBAC + observability) · [ADR-014](docs/adr/ADR-014-cosmosdb-and-servicebus.md) (Cosmos DB + Service Bus)

### 3. Cloud-Native Code · _Status: complete (dev environment)_

The application is migrated from local infrastructure to managed cloud services — managed databases, messaging, identity, and secret storage — adopting token-based authentication and the patterns that make the services genuinely cloud-native. In the dev environment this is complete: secret-less configuration from Key Vault, messaging on Service Bus, the product catalogue on Cosmos DB, resilience and health hardening, Entra ID authentication (including OAuth2 Authorization Code + PKCE for interactive clients, delegated user tokens, and app roles), and the serverless Event Grid → Functions → Azure Communication Services notification path proven end to end.

- **Build guide:** [Cloud Migration Guide](docs/guides/cloud-migration-guide.md)
- **Concepts to read first:** [Cosmos DB](docs/guides/cosmosdb-concepts.md) · [Messaging](docs/guides/messaging-concepts.md) · [Serverless & Eventing](docs/guides/serverless-eventing-concepts.md) · [Identity](docs/guides/identity-concepts.md) · [OAuth2 Authorization Code + PKCE](docs/guides/oauth2-pkce-concepts.md)
- **Decisions:** [ADR-015](docs/adr/ADR-015-messaging-migration-to-service-bus.md) (messaging → Service Bus) · [ADR-016](docs/adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) (Cosmos data migration + workload identity) · [ADR-017](docs/adr/ADR-017-entra-id-functions-eventgrid.md) (Entra ID + Functions + Event Grid) · [ADR-019](docs/adr/ADR-019-serverless-notification-functions-eventgrid.md) (serverless notification) · [ADR-020](docs/adr/ADR-020-api-management-managed-edge-gateway.md) (API Management edge) · [ADR-021](docs/adr/ADR-021-retire-identity-service-for-entra.md) (retire the identity service for Entra)

### 4. Kubernetes Platform · _Status: in progress (containerization complete; cluster pending)_

The services are containerized and run on a managed Kubernetes (AKS) cluster, with ingress, autoscaling, health management, and workload identity. **Containerization is complete:** all six deployable services have multi-stage, non-root Dockerfiles serving on port 8080, a repository-root `.dockerignore`, and images built and published to the Azure Container Registry; the runtime configuration each service expects is catalogued for the cluster rollout. The AKS cluster, Helm packaging, ingress/TLS, in-cluster workload identity, and GitOps delivery are **still to come**.

- **Build guide:** [AKS Guide](docs/guides/aks-guide.md) — container strategy, build-and-push workflow, and the naming conventions (cluster/Helm/GitOps sections are placeholders until delivered)
- **Reference:** [Container Configuration](docs/guides/container-configuration.md) — the runtime configuration keys each service requires (the source for the later Helm values)
- **Concepts to read first:** [Networking & Kubernetes](docs/guides/networking-concepts.md) · [Identity](docs/guides/identity-concepts.md) (workload identity)
- **Decisions:** [ADR-018](docs/adr/ADR-018-aks-workload-identity-base-image.md) (AKS cluster, workload identity, hardened base image)

### 5. DevOps & DevSecOps · _Status: in progress_

Continuous integration and delivery, security and compliance gates, and end-to-end observability tie the platform together and keep it shippable and safe.

- **Build guide:** [DevOps Guide](docs/guides/devops-guide.md) _(in progress)_
- **Concepts to read first:** [Observability design](docs/design/OBSERVABILITY.md)
- **Decisions:** [ADR-022](docs/adr/ADR-022-cicd-github-actions-oidc.md) (CI/CD on GitHub Actions with OIDC federated credentials to Azure)

---

## Scenario & End-to-End Testing

Hands-on, end-to-end functional testing of the running platform — covering each service, the SAGA flows, and the compensation paths — is documented separately in the [Developer Test Guide](docs/test/DevTestGuide.md).
