# Architecture — all 19 diagrams

The full diagram set for the cloud-native AntKart platform, grouped into six tiers. Each entry states the question the diagram answers, then the diagram itself, then decisions and guides to go deeper. Follows the [Diagram Plan](../DIAGRAM-PLAN.md)'s locked visual language.

- **C4 diagrams (01–05, 11)** are authored in [`workspace.dsl`](../workspace.dsl); their PNG renders live in [`../renders/`](../renders/README.md) (exported separately — links show broken until then).
- **Mermaid diagrams (06–10, 12–19)** render natively on GitHub and live in this folder, one file each.
- **Diagram numbers are stable identifiers, not a sort order** — e.g. 19 sits in the DevOps tier though it is numbered after 18.

**Tiers:** [System](#system--what-it-is) · [Cloud](#cloud--where-it-lives) · [Security & identity](#security--identity) · [Kubernetes](#kubernetes--how-it-runs) · [DevOps](#devops--how-it-ships) · [Cross-cutting](#cross-cutting)

---

## System — what it is

### 01 · System context (L1)
> **Question:** Who uses AntKart and what external systems does it depend on?

![01 · System context](../renders/01-system-context.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-001 — Microservices Architecture](../../adr/ADR-001-microservices-architecture.md) · [ADR-021 — Retire the Dedicated Identity Service for Microsoft Entra ID](../../adr/ADR-021-retire-identity-service-for-entra.md) · [ADR-017 — Entra ID, Azure Functions, and Event Grid](../../adr/ADR-017-entra-id-functions-eventgrid.md)

### 02 · Container view (L2) — services, Azure PaaS, APIM
> **Question:** What are the deployable pieces and the managed services behind the edge?

![02 · Container view](../renders/02-containers.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-001 — Microservices Architecture](../../adr/ADR-001-microservices-architecture.md) · [ADR-004 — Polyglot Persistence](../../adr/ADR-004-polyglot-persistence.md) · [ADR-006 — Ocelot API Gateway over YARP](../../adr/ADR-006-ocelot-api-gateway.md) · [ADR-014 — Cosmos DB and Azure Service Bus](../../adr/ADR-014-cosmosdb-and-servicebus.md) · [ADR-019 — Serverless Notification with Azure Functions and Event Grid](../../adr/ADR-019-serverless-notification-functions-eventgrid.md) · [ADR-020 — API Management as the Managed Edge Gateway](../../adr/ADR-020-api-management-managed-edge-gateway.md) _(APIM planned)_

### 03 · Component view (L3) — inside AK.Order
> **Question:** How is AK.Order structured internally (API → application → domain → infrastructure)?

![03 · Component view](../renders/03-order-components.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-002 — Clean Architecture and Domain-Driven Design](../../adr/ADR-002-clean-architecture-and-ddd.md) · [ADR-010 — CQRS and MediatR](../../adr/ADR-010-CQRS-and-MediatR.md) · [ADR-011 — Repository, Specification, and Unit of Work](../../adr/ADR-011-Repository-Specification-and-Unit-of-Work.md) · [ADR-005 — SAGA Orchestration over 2PC and Choreography](../../adr/ADR-005-saga-orchestration.md) · [ADR-009 — Domain Events vs Integration Events](../../adr/ADR-009-domain-events-vs-integration-events.md)

### 04 · Order saga — dynamic flow to Paid
> **Question:** How does an order flow through the orchestrated saga to a Paid state?

![04 · Order saga](../renders/04-saga-flow.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-005 — SAGA Orchestration over 2PC and Choreography](../../adr/ADR-005-saga-orchestration.md) · [ADR-007 — MassTransit over Raw RabbitMQ Client](../../adr/ADR-007-masstransit-over-raw-rabbitmq.md) · [ADR-015 — Messaging Migration to Azure Service Bus](../../adr/ADR-015-messaging-migration-to-service-bus.md) · [Event Bus design](../../design/EVENTBUS.md)

---

## Cloud — where it lives

### 05 · Azure resource topology
> **Question:** What Azure resources exist and how are they grouped/related?

![05 · Azure resource topology](../renders/05-azure-topology.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](../../adr/ADR-012-iac-with-terraform-terragrunt.md) · [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](../../adr/ADR-018-aks-workload-identity-base-image.md) · [Infrastructure Guide](../../guides/infrastructure-guide.md) · [IaC Concepts](../../guides/iac-concepts.md)

### 06 · Terragrunt unit dependency graph
> **Question:** In what order do the IaC units apply, and what depends on what?

View diagram → [06-terragrunt-dependencies.md](06-terragrunt-dependencies.md)

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](../../adr/ADR-012-iac-with-terraform-terragrunt.md) · [IaC Concepts](../../guides/iac-concepts.md) · [Infrastructure Guide](../../guides/infrastructure-guide.md)

### 07 · Network & traffic path
> **Question:** How does a request physically reach a service, and where is TLS terminated?

View diagram → [07-network-traffic-path.md](07-network-traffic-path.md)

**Go deeper:** [Networking & Kubernetes Concepts](../../guides/networking-concepts.md) · [AKS Guide](../../guides/aks-guide.md) · [ADR-006 — Ocelot API Gateway over YARP](../../adr/ADR-006-ocelot-api-gateway.md)

---

## Security & identity

### 08 · Identity & trust chain
> **Question:** How does trust flow from provisioning to runtime, secret-lessly?

View diagram → [08-identity-chain.md](08-identity-chain.md)

**Go deeper:** [ADR-016 — Cosmos DB Data Migration and Workload Identity Foundation](../../adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](../../adr/ADR-022-cicd-github-actions-oidc.md) · [Identity Concepts](../../guides/identity-concepts.md)

### 09 · Security posture & trust boundaries
> **Question:** What is exposed vs internal, and where do the known gaps sit?

View diagram → [09-security-posture.md](09-security-posture.md)

**Go deeper:** [Known Issues Register](../../KNOWN_ISSUES.md) · [Identity Concepts](../../guides/identity-concepts.md) · [Security Test Guide](../../test/SECURITY_TESTS.md)

### 10 · APIM edge & two-gateway model (target state)
> **Question:** What does the managed edge do before traffic reaches the cluster ingress?

View diagram → [10-apim-edge.md](10-apim-edge.md)

_Azure API Management is **planned**, not yet provisioned._

**Go deeper:** [ADR-020 — API Management as the Managed Edge Gateway](../../adr/ADR-020-api-management-managed-edge-gateway.md) · [ADR-006 — Ocelot API Gateway over YARP](../../adr/ADR-006-ocelot-api-gateway.md)

---

## Kubernetes — how it runs

### 11 · Cluster topology
> **Question:** How are namespaces, workloads, and ingress laid out inside AKS?

![11 · Cluster topology](../renders/11-cluster-topology.png)
_Rendered from workspace.dsl — see [renders/README.md](../renders/README.md)_

**Go deeper:** [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](../../adr/ADR-018-aks-workload-identity-base-image.md) · [AKS Guide](../../guides/aks-guide.md)

### 12 · Workload identity token flow
> **Question:** How does a pod get an Entra token with no stored secret?

View diagram → [12-workload-identity-token-flow.md](12-workload-identity-token-flow.md)

**Go deeper:** [ADR-016 — Cosmos DB Data Migration and Workload Identity Foundation](../../adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [ADR-018 — Managed Kubernetes, Workload Identity, and Hardened Base Image](../../adr/ADR-018-aks-workload-identity-base-image.md) · [Identity Concepts](../../guides/identity-concepts.md)

### 13 · Helm chart & values precedence
> **Question:** How does one generic chart become six services, and which values win?

View diagram → [13-helm-chart-values.md](13-helm-chart-values.md)

**Go deeper:** [AKS Guide](../../guides/aks-guide.md) · [Container Configuration](../../guides/container-configuration.md) · [GitOps Guide](../../guides/gitops-guide.md)

---

## DevOps — how it ships

### 14 · CI pipeline
> **Question:** What runs on a pull request, and what gates the merge?

View diagram → [14-ci-pipeline.md](14-ci-pipeline.md)

**Go deeper:** [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](../../adr/ADR-022-cicd-github-actions-oidc.md) · [ADR-023 — CI/CD Pipeline Design and Repository Strategy](../../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](../../guides/devops-cicd-guide.md)

### 15 · CD pipeline
> **Question:** What happens on merge — build, push, tag-bump — and with what identity?

View diagram → [15-cd-pipeline.md](15-cd-pipeline.md)

**Go deeper:** [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](../../adr/ADR-022-cicd-github-actions-oidc.md) · [ADR-023 — CI/CD Pipeline Design and Repository Strategy](../../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](../../guides/devops-cicd-guide.md)

### 16 · GitOps reconciliation loop
> **Question:** How does a Git change become a running pod via Argo CD?

View diagram → [16-gitops-reconciliation.md](16-gitops-reconciliation.md)

**Go deeper:** [ADR-023 — CI/CD Pipeline Design and Repository Strategy](../../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [GitOps Guide](../../guides/gitops-guide.md)

### 17 · Environment promotion — dev vs QA
> **Question:** How does a change move from dev to QA, and what differs between them?

View diagram → [17-env-promotion.md](17-env-promotion.md)

_The QA environment is **planned**; only `dev` exists today._

**Go deeper:** [ADR-012 — Infrastructure as Code with Terraform and Terragrunt](../../adr/ADR-012-iac-with-terraform-terragrunt.md) · [Infrastructure Guide](../../guides/infrastructure-guide.md) · [Roadmap](../../ROADMAP.md)

### 19 · Delivery architecture — commit to running pod
> **Question:** How does a commit become a running pod?

View diagram → [19-delivery-architecture.md](19-delivery-architecture.md)

**Go deeper:** [ADR-022 — CI/CD on GitHub Actions with OIDC Federated Credentials](../../adr/ADR-022-cicd-github-actions-oidc.md) · [ADR-023 — CI/CD Pipeline Design and Repository Strategy](../../adr/ADR-023-cicd-pipeline-design-and-repository-strategy.md) · [DevOps CI/CD Guide](../../guides/devops-cicd-guide.md) · [GitOps Guide](../../guides/gitops-guide.md)

---

## Cross-cutting

### 18 · Observability pipeline
> **Question:** How do logs, metrics, and traces flow to their sinks and dashboards?

View diagram → [18-observability.md](18-observability.md)

_Structured logging to Application Insights / Log Analytics is **delivered**; metrics and tracing (OpenTelemetry, Prometheus, Grafana) are **planned**._

**Go deeper:** [ADR-013 — Key Vault RBAC and Observability Foundation](../../adr/ADR-013-key-vault-rbac-and-observability-foundation.md) · [Observability design](../../design/OBSERVABILITY.md)
