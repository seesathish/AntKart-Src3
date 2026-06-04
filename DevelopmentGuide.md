# Development Guide

This guide is the entry point to how the AntKart platform is built and evolved through its cloud-native transformation. It gives the big picture and links out to focused deep-dive guides for each area.

It is a **map, not the detail** — each delivery phase below has a dedicated guide that captures the concepts, the scripts, the execution, and the verification for that area. For the reasoning behind the architecture, see the [Architecture Decision Records index](docs/adr/README.md); for the architecture overview and diagrams, see the [README](README.md#architecture-overview).

---

## Delivery Phases

The platform is delivered as a series of phases. Each phase builds on the previous one and is documented in its own deep-dive guide.

### 1. Foundation

The clean application baseline: independently deployable .NET microservices built with Clean Architecture, Domain-Driven Design, CQRS, an event-driven SAGA, an API gateway, resilience, and structured observability. This phase establishes the architecture decisions and the repository structure that everything else builds on.

→ [Architecture Decision Records](docs/adr/README.md) · [Cross-cutting design docs](docs/design/) · [Architecture overview](README.md#architecture-overview)

### 2. Cloud Infrastructure (IaC)

All cloud resources are provisioned as code with Terraform and Terragrunt — networking, container registry, secrets, data stores, messaging, and supporting services — so the environment is reproducible, reviewable, and version-controlled.

→ [Infrastructure Guide](docs/guides/infrastructure-guide.md) · [Networking & Kubernetes Concepts](docs/guides/networking-concepts.md) · [Cosmos DB Concepts](docs/guides/cosmosdb-concepts.md)

### 3. Cloud-Native Code

The application is migrated from local infrastructure to managed cloud services — managed databases, messaging, identity, and secret storage — adopting token-based authentication and the patterns that make the services genuinely cloud-native.

→ [Cloud Migration Guide](docs/guides/cloud-migration-guide.md) _(in progress)_

### 4. Kubernetes Platform

The services are containerized and run on a managed Kubernetes (AKS) cluster, with ingress, autoscaling, health management, and workload identity.

→ [AKS Guide](docs/guides/aks-guide.md) _(in progress)_

### 5. DevOps & DevSecOps

Continuous integration and delivery, security and compliance gates, and end-to-end observability tie the platform together and keep it shippable and safe.

→ [DevOps Guide](docs/guides/devops-guide.md) _(in progress)_

---

## Scenario & End-to-End Testing

Hands-on, end-to-end functional testing of the running platform — covering each service, the SAGA flows, and the compensation paths — is documented separately in the [Developer Test Guide](docs/test/DevTestGuide.md).
