# AntKart — cloud-native e-commerce platform

AntKart is a **.NET 9** e-commerce platform of **six microservices** plus a **serverless notifications app**, running on **Azure Kubernetes Service**, provisioned with **Terraform and Terragrunt**, and delivered by **GitHub Actions and Argo CD**. This page is the front door: one diagram per topic, each linking into the [Development Guide](DevelopmentGuide.md) for the detail.

**Live:** [https://api.antkart.in](https://api.antkart.in) — served over a trusted Let's Encrypt production TLS certificate. (An earlier Phase-1 build ran locally on Docker Compose in a separate repository; this repository is the cloud-native platform.)

## System overview

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/C4Renders/renders/SystemOverview-dark.svg">
  <img alt="AntKart system overview: two actors reaching six services and a serverless notification app through a single API gateway, with Microsoft Entra ID, Razorpay and Azure Communication Services as external dependencies" src="docs/C4Renders/renders/SystemOverview.svg">
</picture>

Customers reach the platform through one public HTTPS endpoint; behind it, six services and a serverless notifications app coordinate over Azure Service Bus. Everything it touches — identity, payments, email, certificates, DNS — is a managed external system. Start with the [Development Guide](DevelopmentGuide.md) for how it is built, or read on for one topic at a time.

## Platform architecture

> **Diagram: System overview** — _not yet drawn_
> **Must show:** the engineering foundation inside a service - Clean Architecture layers, CQRS with MediatR, the orchestrated saga, the transactional outbox, repository with specification and unit of work, minimal API endpoints, and the distinction between domain events and integration events. Answers "how is the code built".

Every service is the same inside: a dependency-free domain core, an application layer of CQRS handlers behind a validation pipeline, and a thin API host. Services never call each other synchronously for business flows — they coordinate through an orchestrated saga with a transactional outbox.

→ [Platform architecture](docs/development/0-platform-architecture.md)

## Infrastructure as code

> **Diagram: Infrastructure as code** — _not yet drawn_
> **Must show:** Terraform driven by Terragrunt - root.hcl generating backend, provider and versions into each of the eighteen units, units composed from shared modules, remote state isolated per unit in Azure Storage with blob-lease locking, and the dev environment with QA marked planned. Answers "how does the cloud get built".

Every Azure resource is provisioned as code: Terraform modules describe how a resource is built, and Terragrunt live units wire them together per environment. A shared `root.hcl` generates the backend and provider config into each unit, and remote state is isolated per unit with blob-lease locking.

→ [Infrastructure as code](docs/development/1-infrastructure-as-code.md)

## Azure services

> **Diagram: Azure services** — _not yet drawn_
> **Must show:** the resource estate - resource group, region, virtual network, AKS, and the managed services: Cosmos DB, PostgreSQL Flexible Server, Managed Redis, Service Bus, Event Grid, Functions, Key Vault, Communication Services, Container Registry, Application Insights and Log Analytics, with API Management marked planned. Where useful, show which Phase 1 component each replaced. Answers "where does it run".

The platform runs entirely on managed Azure services — Cosmos DB, PostgreSQL, Managed Redis, Service Bus, Event Grid, Functions, Key Vault, and more. Each replaced a local Phase-1 component, adopting token-based authentication throughout. API Management, the managed edge, is planned.

→ [Azure services](docs/development/2-azure-services.md)

## Kubernetes

> **Diagram: Kubernetes** — _not yet drawn_
> **Must show:** the cluster and node pool, the namespaces antkart, ingress-nginx, cert-manager and argocd, the deployments and their replica counts, which services are ClusterIP-only versus reachable through ingress, TLS termination, and the public path to api.antkart.in. Answers "how is it orchestrated".

The six services run on a managed AKS cluster with Azure CNI Overlay and an OIDC issuer, deployed from one generic Helm chart parameterised per service. Only the gateway is exposed through ingress with cert-manager TLS; the rest are ClusterIP-only. Pods reach Azure with no stored secret via workload identity.

→ [Kubernetes](docs/development/3-kubernetes.md)

## DevOps

> **Diagram: DevOps** — _not yet drawn_
> **Must show:** commit to running pod - pull request, the quality gate of build, test, SonarCloud and Trivy with four required checks and branch protection, merge, CD authenticating to Azure by OIDC with no stored secret, an image tagged with the commit SHA, push to Container Registry, the tag bump committed back to Git, and Argo CD auto-sync with self-heal. Answers "how does it ship".

Delivery is a CI quality gate on every pull request and a CD pipeline on merge. CD never touches the cluster — it authenticates to Azure by OIDC, pushes a commit-SHA-tagged image, and bumps the tag in Git for Argo CD to reconcile. No cluster credentials live in CI/CD.

→ [DevOps](docs/development/4-devops.md)

## Observability

> **Diagram: Observability** — _not yet drawn_
> **Must show:** Serilog structured logging and OpenTelemetry feeding Application Insights and Log Analytics, plus Prometheus scraping and Grafana dashboards. Mark clearly what is delivered and what is planned. Answers "how do you know it is working".

Structured logging is delivered: every service and Function emits Serilog logs to the console, collected in the cloud by Application Insights and Log Analytics. Distributed tracing and metrics — OpenTelemetry, Prometheus, and Grafana — are planned, not yet wired.

→ [Observability](docs/development/5-observability.md)

## Security

> **Diagram: Security** — _not yet drawn_
> **Must show:** the secret-less chain end to end - Entra ID tokens validated at the edge, per-service workload identity with federated credentials, Key Vault, data-plane RBAC scoped to individual resources, OIDC federated credentials for CI/CD with no stored secrets, TLS termination, the trust boundary between public and ClusterIP-only services, and known gaps including KI-002. Answers "how is it secured".

Security rests on no stored secrets anywhere and defence in depth on tokens: every identity authenticates through federation with least-privilege RBAC, and the Entra bearer token is validated at the edge and again inside each service. One tracked gap remains (KI-002), and the managed edge is a planned addition.

→ [Security](docs/development/6-security.md)

## Explore

- [Development Guide](DevelopmentGuide.md) — how the platform is built, layer by layer.
- [Test Guide](DevTestGuide.md) — how it is verified, from service code to full-cloud end-to-end.
- [Architecture decisions](docs/adr/README.md) — the ADRs and why each choice was made.
- [Known Issues Register](docs/KNOWN_ISSUES.md) — open defects and deferred fixes, notably KI-002 and KI-005.
