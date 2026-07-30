# AntKart — cloud-native e-commerce platform

AntKart is a **.NET 9** e-commerce platform of **six microservices** plus a **serverless notifications app**, running on **Azure Kubernetes Service**, provisioned with **Terraform and Terragrunt**, and delivered by **GitHub Actions and Argo CD**. It is reachable at **[https://api.antkart.in](https://api.antkart.in)** over a trusted TLS certificate. (An earlier Phase-1 build ran locally on Docker Compose in a separate repository; this repository is the cloud-native platform.)

This README explains the platform **through its 18 architecture diagrams**, in order, with the decisions (ADRs) and guides linked under each so you can go deeper.

## At a glance

| | |
|---|---|
| **Language & runtime** | .NET 9 (C#) — six REST/gRPC microservices + a serverless notifications app (Azure Functions, isolated worker) |
| **Cloud** | Microsoft Azure |
| **Orchestration** | Azure Kubernetes Service (AKS) — Azure CNI Overlay, OIDC issuer, workload identity |
| **Infrastructure as code** | Terraform modules composed by Terragrunt live units |
| **CI/CD** | GitHub Actions — OIDC to Azure, no stored cloud secrets |
| **GitOps** | Argo CD — auto-sync + self-heal from Git |
| **Messaging** | Azure Service Bus via MassTransit (orchestrated SAGA) |
| **Data stores** | Cosmos DB (MongoDB API), PostgreSQL Flexible Server, Azure Managed Redis |
| **Identity** | Microsoft Entra ID — workload identity + federated credentials, no stored secrets |
| **Edge** | ingress-nginx + cert-manager TLS at `api.antkart.in` (Azure API Management is **planned**) |

## Contents

- [System — what it is](#system--what-it-is) · diagrams 01–04
- [Cloud — where it lives](#cloud--where-it-lives) · diagrams 05–07
- [Security & identity](#security--identity) · diagrams 08–10
- [Kubernetes — how it runs](#kubernetes--how-it-runs) · diagrams 11–13
- [DevOps — how it ships](#devops--how-it-ships) · diagrams 14–17
- [Cross-cutting](#cross-cutting) · diagram 18
- [Running and rebuilding the platform](#running-and-rebuilding-the-platform)
- [Documentation map](#documentation-map)
- [Known issues](#known-issues)

---

## System — what it is

The application shape: who uses it, the deployable pieces, one service's internals, and the order saga.

### 01 · System context (L1)
> **Question:** Who uses AntKart and what external systems does it depend on?

![01 · System context](docs/architecture/renders/01-system-context.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-001 — Microservices Architecture](docs/adr/ADR-001-microservices-architecture.md) · [ADR-021 — Retire the Dedicated Identity Service for Microsoft Entra ID](docs/adr/ADR-021-retire-identity-service-for-entra.md) · [ADR-017 — Entra ID, Azure Functions, and Event Grid](docs/adr/ADR-017-entra-id-functions-eventgrid.md)

### 02 · Container view (L2) — services, Azure PaaS, APIM
> **Question:** What are the deployable pieces and the managed services behind the edge?

![02 · Container view](docs/architecture/renders/02-containers.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-001 — Microservices Architecture](docs/adr/ADR-001-microservices-architecture.md) · [ADR-004 — Polyglot Persistence](docs/adr/ADR-004-polyglot-persistence.md) · [ADR-006 — Ocelot API Gateway over YARP](docs/adr/ADR-006-ocelot-api-gateway.md) · [ADR-014 — Cosmos DB and Azure Service Bus](docs/adr/ADR-014-cosmosdb-and-servicebus.md) · [ADR-019 — Serverless Notification with Azure Functions and Event Grid](docs/adr/ADR-019-serverless-notification-functions-eventgrid.md) · [ADR-020 — API Management as the Managed Edge Gateway](docs/adr/ADR-020-api-management-managed-edge-gateway.md) _(APIM planned)_

### 03 · Component view (L3) — inside AK.Order
> **Question:** How is AK.Order structured internally (API → application → domain → infrastructure)?

![03 · Component view](docs/architecture/renders/03-order-components.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-002 — Clean Architecture and Domain-Driven Design](docs/adr/ADR-002-clean-architecture-and-ddd.md) · [ADR-010 — CQRS and MediatR](docs/adr/ADR-010-CQRS-and-MediatR.md) · [ADR-011 — Repository, Specification, and Unit of Work](docs/adr/ADR-011-Repository-Specification-and-Unit-of-Work.md) · [ADR-005 — SAGA Orchestration over 2PC and Choreography](docs/adr/ADR-005-saga-orchestration.md) · [ADR-009 — Domain Events vs Integration Events](docs/adr/ADR-009-domain-events-vs-integration-events.md)

### 04 · Order saga — dynamic flow to Paid
> **Question:** How does an order flow through the orchestrated saga to a Paid state?

![04 · Order saga](docs/architecture/renders/04-saga-flow.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-005 — SAGA Orchestration over 2PC and Choreography](docs/adr/ADR-005-saga-orchestration.md) · [ADR-007 — MassTransit over Raw RabbitMQ Client](docs/adr/ADR-007-masstransit-over-raw-rabbitmq.md) · [ADR-015 — Messaging Migration to Azure Service Bus](docs/adr/ADR-015-messaging-migration-to-service-bus.md) · [Event Bus design](docs/design/EVENTBUS.md)

---

## Cloud — where it lives

The Azure resources, how they are provisioned as code, and how traffic reaches them.

### 05 · Azure resource topology
> **Question:** What Azure resources exist and how are they grouped/related?

![05 · Azure resource topology](docs/architecture/renders/05-azure-topology.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](docs/adr/ADR-012-iac-with-terraform-terragrunt.md) · [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](docs/adr/ADR-018-aks-workload-identity-base-image.md) · [Infrastructure Guide](docs/guides/infrastructure-guide.md) · [IaC Concepts](docs/guides/iac-concepts.md)

### 06 · Terragrunt unit dependency graph
> **Question:** In what order do the IaC units apply, and what depends on what?

View diagram → [06-terragrunt-dependencies.md](docs/architecture/diagrams/06-terragrunt-dependencies.md)

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](docs/adr/ADR-012-iac-with-terraform-terragrunt.md) · [IaC Concepts](docs/guides/iac-concepts.md) · [Infrastructure Guide](docs/guides/infrastructure-guide.md)

### 07 · Network & traffic path
> **Question:** How does a request physically reach a service, and where is TLS terminated?

View diagram → [07-network-traffic-path.md](docs/architecture/diagrams/07-network-traffic-path.md)

**Go deeper:** [Networking & Kubernetes Concepts](docs/guides/networking-concepts.md) · [AKS Guide](docs/guides/aks-guide.md) · [ADR-006 — Ocelot API Gateway over YARP](docs/adr/ADR-006-ocelot-api-gateway.md)

---

## Security & identity

The secret-less trust chain, the public/internal boundaries, and the planned managed edge.

### 08 · Identity & trust chain
> **Question:** How does trust flow from provisioning to runtime, secret-lessly?

View diagram → [08-identity-chain.md](docs/architecture/diagrams/08-identity-chain.md)

**Go deeper:** [ADR-016 — Cosmos DB Data Migration and Workload Identity Foundation](docs/adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](docs/adr/ADR-022-cicd-github-actions-oidc.md) · [Identity Concepts](docs/guides/identity-concepts.md)

### 09 · Security posture & trust boundaries
> **Question:** What is exposed vs internal, and where do the known gaps sit?

View diagram → [09-security-posture.md](docs/architecture/diagrams/09-security-posture.md)

**Go deeper:** [Known Issues Register](docs/KNOWN_ISSUES.md) · [Identity Concepts](docs/guides/identity-concepts.md) · [Security Test Guide](docs/test/SECURITY_TESTS.md)

### 10 · APIM edge & two-gateway model (target state)
> **Question:** What does the managed edge do before traffic reaches the cluster ingress?

View diagram → [10-apim-edge.md](docs/architecture/diagrams/10-apim-edge.md)

_Azure API Management is **planned**, not yet provisioned._

**Go deeper:** [ADR-020 — API Management as the Managed Edge Gateway](docs/adr/ADR-020-api-management-managed-edge-gateway.md) · [ADR-006 — Ocelot API Gateway over YARP](docs/adr/ADR-006-ocelot-api-gateway.md)

---

## Kubernetes — how it runs

The cluster layout, how pods authenticate without secrets, and how one chart deploys six services.

### 11 · Cluster topology
> **Question:** How are namespaces, workloads, and ingress laid out inside AKS?

![11 · Cluster topology](docs/architecture/renders/11-cluster-topology.png)
_Rendered from workspace.dsl — see [docs/architecture/renders/README.md](docs/architecture/renders/README.md)_

**Go deeper:** [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](docs/adr/ADR-018-aks-workload-identity-base-image.md) · [AKS Guide](docs/guides/aks-guide.md)

### 12 · Workload identity token flow
> **Question:** How does a pod get an Entra token with no stored secret?

View diagram → [12-workload-identity-token-flow.md](docs/architecture/diagrams/12-workload-identity-token-flow.md)

**Go deeper:** [ADR-016 — Cosmos DB Data Migration and Workload Identity Foundation](docs/adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](docs/adr/ADR-018-aks-workload-identity-base-image.md) · [Identity Concepts](docs/guides/identity-concepts.md)

### 13 · Helm chart & values precedence
> **Question:** How does one generic chart become six services, and which values win?

View diagram → [13-helm-chart-values.md](docs/architecture/diagrams/13-helm-chart-values.md)

**Go deeper:** [AKS Guide](docs/guides/aks-guide.md) · [Container Configuration](docs/guides/container-configuration.md) · [GitOps Guide](docs/guides/gitops-guide.md)

---

## DevOps — how it ships

The pull-request gate, the merge delivery, the GitOps loop that runs it, and environment promotion.

### 14 · CI pipeline
> **Question:** What runs on a pull request, and what gates the merge?

View diagram → [14-ci-pipeline.md](docs/architecture/diagrams/14-ci-pipeline.md)

**Go deeper:** [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](docs/adr/ADR-022-cicd-github-actions-oidc.md) · [ADR-023 — CI/CD Pipeline Design and Repository Strategy](docs/adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](docs/guides/devops-cicd-guide.md)

### 15 · CD pipeline
> **Question:** What happens on merge — build, push, tag-bump — and with what identity?

View diagram → [15-cd-pipeline.md](docs/architecture/diagrams/15-cd-pipeline.md)

**Go deeper:** [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](docs/adr/ADR-022-cicd-github-actions-oidc.md) · [ADR-023 — CI/CD Pipeline Design and Repository Strategy](docs/adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](docs/guides/devops-cicd-guide.md)

### 16 · GitOps reconciliation loop
> **Question:** How does a Git change become a running pod via Argo CD?

View diagram → [16-gitops-reconciliation.md](docs/architecture/diagrams/16-gitops-reconciliation.md)

**Go deeper:** [ADR-023 — CI/CD Pipeline Design and Repository Strategy](docs/adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [GitOps Guide](docs/guides/gitops-guide.md)

### 17 · Environment promotion — dev vs QA
> **Question:** How does a change move from dev to QA, and what differs between them?

View diagram → [17-env-promotion.md](docs/architecture/diagrams/17-env-promotion.md)

_The QA environment is **planned**; only `dev` exists today._

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](docs/adr/ADR-012-iac-with-terraform-terragrunt.md) · [Infrastructure Guide](docs/guides/infrastructure-guide.md) · [Roadmap](docs/ROADMAP.md)

---

## Cross-cutting

How the platform is observed.

### 18 · Observability pipeline
> **Question:** How do logs, metrics, and traces flow to their sinks and dashboards?

View diagram → [18-observability.md](docs/architecture/diagrams/18-observability.md)

_Structured logging to Application Insights / Log Analytics is **delivered**; metrics and tracing (OpenTelemetry, Prometheus, Grafana) are **planned**._

**Go deeper:** [ADR-013 — Key Vault RBAC and Observability Foundation](docs/adr/ADR-013-key-vault-rbac-and-observability-foundation.md) · [Observability design](docs/design/OBSERVABILITY.md)

---

## Running and rebuilding the platform

- **[Infrastructure Guide](docs/guides/infrastructure-guide.md)** — provision the Azure resources as code, unit by unit (Understand → Build → Execute → Verify).
- **[AKS Guide](docs/guides/aks-guide.md)** — containers, the cluster, workload identity, Helm deployment, ingress/TLS, and troubleshooting.
- **[Operations Command Reference](docs/guides/operations-command-reference.md)** — every `az` / `kubectl` / `helm` / `terragrunt` / `argocd` command to build, inspect, and operate the platform, each flag explained.

## Documentation map

| Document | What it is for |
|----------|----------------|
| [Development Guide](DevelopmentGuide.md) | The spine — each delivery phase with its build guide, prerequisite concepts, and governing ADRs. |
| [Roadmap](docs/ROADMAP.md) | The single record of what is delivered, in progress, and planned. |
| [ADR index](docs/adr/README.md) | The full set of Architecture Decision Records and why each choice was made. |
| [Diagram Plan](docs/architecture/DIAGRAM-PLAN.md) | The plan and contract for the 18-diagram set (visual language, tooling, status). |
| [Known Issues Register](docs/KNOWN_ISSUES.md) | Acknowledged defects and deferred fixes, each with a planned resolution. |
| [Testing index](docs/test/README.md) | The verification strategy — unit, integration, end-to-end, and security tests. |

## Known issues

Open defects are tracked in the **[Known Issues Register](docs/KNOWN_ISSUES.md)**. The two headline items:

- **KI-002 (High)** — the Discount gRPC service **decodes** the bearer token without verifying its signature/issuer/audience; mitigated only by being ClusterIP-only (not externally reachable).
- **KI-005 (Medium)** — **no stock-release compensation on payment failure**: stock reserved by the saga is retained indefinitely when a payment fails, pending a compensation workstream.

The register also tracks **KI-003** (permissive gateway CORS) and **KI-004** (mutable image tag can serve a stale image).
