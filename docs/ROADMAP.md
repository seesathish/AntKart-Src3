# AntKart Platform Roadmap

## Purpose and how to use this document

This is the single authoritative record of what the AntKart platform has **delivered**, what is **in progress**, and what is **planned**. It exists so that any reader can understand the full state of the platform from one file and then follow links into the detailed guides and Architecture Decision Records for depth. It summarises and links; it does not restate detail that already lives in the guides. Status is expressed only as **Delivered**, **In progress**, or **Planned**.

**Start here:** the [Development Guide](../DevelopmentGuide.md) is the spine of the documentation (each delivery phase with its build guide, prerequisite concepts, and governing ADRs); the [Architecture (C4) reference](architecture/C4Architecture.md) and its [Structurizr workspace](architecture/workspace.dsl) hold the diagrams; the [AKS Guide](guides/aks-guide.md) covers containerization, the cluster, and workload identity.

## Platform at a glance

AntKart is a cloud-native microservices e-commerce platform, built as a reference implementation to demonstrate enterprise cloud-native practice end to end — Clean Architecture, an event-driven SAGA, infrastructure as code, a secret-less identity model, and a managed Kubernetes runtime.

It comprises **six deployable services** plus a **serverless notifications** application:

| Component | Transport | Primary store |
|-----------|-----------|---------------|
| [AK.Products](../AK.Products/PRODUCTS_TECHNICAL_DESIGN.md) | REST (Minimal API) | Cosmos DB (MongoDB API) |
| [AK.Discount](../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md) | gRPC | PostgreSQL |
| [AK.ShoppingCart](../AK.ShoppingCart/SHOPPING_CART_TECHNICAL_DESIGN.md) | REST (Minimal API) | Redis |
| [AK.Order](../AK.Order/ORDER_TECHNICAL_DESIGN.md) | REST (Minimal API) | PostgreSQL + SAGA |
| [AK.Payments](../AK.Payments/PAYMENTS_TECHNICAL_DESIGN.md) | REST (Minimal API) | PostgreSQL + Razorpay |
| [AK.Gateway](../AK.Gateway/API_GATEWAY.md) | Ocelot API gateway | — |
| [AK.Notification](guides/cloud-migration-guide.md) | Serverless (Event Grid → Azure Functions) | PostgreSQL (history) + ACS Email |

Core Azure services in use: **Microsoft Entra ID**, **Azure Kubernetes Service**, **Azure Container Registry**, **Azure Cosmos DB**, **Azure Database for PostgreSQL Flexible Server**, **Azure Managed Redis**, **Azure Service Bus**, **Azure Event Grid**, **Azure Functions**, **Azure Communication Services**, **Azure Key Vault**, and **Azure Monitor / Log Analytics**.

## Delivered

**Microservices foundation**
- Independently deployable microservices, each owning its data and deployment lifecycle — [ADR-001](adr/ADR-001-microservices-architecture.md)
- Clean Architecture and Domain-Driven Design per service — [ADR-002](adr/ADR-002-clean-architecture-and-ddd.md)
- CQRS with a mediator pipeline and validation behaviour — [ADR-010](adr/ADR-010-CQRS-and-MediatR.md)
- Repository, Specification, and Unit of Work persistence abstractions — [ADR-011](adr/ADR-011-Repository-Specification-and-Unit-of-Work.md)
- Orchestrated SAGA with a transactional outbox for at-least-once, dual-write-safe eventing — [ADR-005](adr/ADR-005-saga-orchestration.md) · [Event Bus design](design/EVENTBUS.md)
- Domain events (in-process) and integration events (cross-service) as two distinct patterns — [ADR-009](adr/ADR-009-domain-events-vs-integration-events.md)
- In-cluster API gateway (Ocelot) — routing, JWT passthrough, per-route rate limiting and QoS; the internal gateway in the two-gateway edge model — [ADR-006](adr/ADR-006-ocelot-api-gateway.md)
- gRPC for synchronous service-to-service calls (the discount lookup) — [AK.Discount design](../AK.Discount/DISCOUNT_TECHNICAL_DESIGN.md)
- Polly-based resilience (retry, circuit breaker, timeout) on outbound dependencies — [ADR-003](adr/ADR-003-fault-tolerance-with-polly.md) · [Resilience design](design/RESILIENCE.md)
- Polyglot persistence — one store per service, each chosen to fit its workload — [ADR-004](adr/ADR-004-polyglot-persistence.md)
- Shared cross-cutting library (DDD base types, auth, messaging, resilience, middleware) — [ADR-008](adr/ADR-008-shared-ddd-contracts-in-buildingblocks.md) · [Building Blocks](../AK.BuildingBlocks/BUILDING_BLOCKS.md)

**Identity**
- Microsoft Entra ID as the identity provider; each service validates tokens and authorizes from the flat `roles` claim; the application identity service is retired — [ADR-021](adr/ADR-021-retire-identity-service-for-entra.md) · [ADR-017](adr/ADR-017-entra-id-functions-eventgrid.md)
- OAuth2 Authorization Code with PKCE for interactive (public) clients, delegated user tokens, app roles, and the `email` optional claim used for user-derived notification recipients — [OAuth2 + PKCE concepts](guides/oauth2-pkce-concepts.md) · [Cloud Migration Guide](guides/cloud-migration-guide.md)

**Cloud data and messaging**
- Product catalogue on Azure Cosmos DB (MongoDB API), connection string sourced from Key Vault — [ADR-016](adr/ADR-016-data-migration-cosmosdb-and-workload-identity.md) · [Cosmos DB concepts](guides/cosmosdb-concepts.md)
- Azure Database for PostgreSQL Flexible Server and Azure Managed Redis provisioned as code — [Infrastructure Guide](guides/infrastructure-guide.md) · [infrastructure/README](../infrastructure/README.md)
- Messaging on Azure Service Bus with MassTransit, over Entra token auth against IaC-owned topology — [ADR-014](adr/ADR-014-cosmosdb-and-servicebus.md) (Cosmos + Service Bus provisioning) · [ADR-015](adr/ADR-015-messaging-migration-to-service-bus.md) · [ADR-007](adr/ADR-007-masstransit-over-raw-rabbitmq.md) · [Messaging concepts](guides/messaging-concepts.md)

**Serverless notifications**
- Event Grid custom topic → Azure Functions → Azure Communication Services email, proven end to end, with managed-identity publishing and sending — [ADR-019](adr/ADR-019-serverless-notification-functions-eventgrid.md) · [Serverless & Eventing concepts](guides/serverless-eventing-concepts.md) · [Cloud Migration Guide](guides/cloud-migration-guide.md)

**Order pricing integrity**
- Server-authoritative pricing: the catalogue is the source of truth, client-submitted prices are advisory, only a price increase interrupts the customer (409), a missing/inactive product returns 422, and an unreachable catalogue fails closed (503) — [AK.Order design](../AK.Order/ORDER_TECHNICAL_DESIGN.md)

**Containerization**
- Multi-stage, non-root images (port 8080) for all six services, a repository-root `.dockerignore`, and images published to the Azure Container Registry — [AKS Guide](guides/aks-guide.md#container-strategy) · [Container Configuration](guides/container-configuration.md)

**Kubernetes**
- AKS cluster `aks-antkart-dev` provisioned by Terraform — Azure CNI Overlay, OIDC issuer and workload identity enabled at creation, Azure RBAC, OMS agent, and kubelet AcrPull for credential-free image pulls — [AKS Guide](guides/aks-guide.md#the-aks-cluster) · [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md)

**Secret-less workload identity**
- One user-assigned managed identity per service with a federated credential trusting the cluster OIDC issuer, and a least-privilege role matrix; verified reading Key Vault from a pod with no stored secret — [AKS Guide](guides/aks-guide.md#workload-identity) · [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md)

**Kubernetes deployment (Helm)**
- All six services run on AKS via Helm — a single generic chart instantiated per service, with workload-identity ServiceAccounts, health probes on `/health/live` and `/health/ready`, resource requests/limits, and ClusterIP services — [Helm charts](../deploy/helm/README.md) · [AKS Guide](guides/aks-guide.md)

**Infrastructure as code**
- Terraform modules (resource shape) composed by Terragrunt live units per environment over a shared remote-state backend, with a reviewed `plan` before every `apply` — [infrastructure/README](../infrastructure/README.md) · [Infrastructure Guide](guides/infrastructure-guide.md) · [ADR-012](adr/ADR-012-iac-with-terraform-terragrunt.md) · [ADR-013](adr/ADR-013-key-vault-rbac-and-observability-foundation.md) (Key Vault RBAC + observability foundation)

**Documentation**
- Concept primers, the full Architecture Decision Record set, and step-by-step build guides — [ADR index](adr/README.md) · [Development Guide](../DevelopmentGuide.md)

## In progress

- **Ingress and cert-manager TLS** — a self-managed ingress-nginx controller exposing the **gateway only**, with cert-manager (Let's Encrypt) automated TLS and a nip.io development hostname. The chart Ingress template, staging/production ClusterIssuers, and the apply runbook are prepared and being applied to the cluster — [AKS Guide](guides/aks-guide.md#ingress-and-tls).

## Planned — near term

- **Azure API Management (managed external edge)** — Azure API Management as the managed edge in a **two-gateway model**: APIM owns edge concerns (TLS termination, JWT validation, rate limiting and quotas, subscription keys and products, developer portal, request/response transformation) while the cluster's internal ingress handles routing to services. These are **sequenced layers, not competing gateways** — the internal cluster ingress (In progress, above) is a prerequisite and is delivered first, then APIM is added in front of it as the managed edge. In-service JWT validation is unchanged (defence in depth) — [ADR-020](adr/ADR-020-api-management-managed-edge-gateway.md).
- **End-to-end verification on the cluster** — exercising the full order journey and SAGA compensation paths against the services running in AKS.
- **Kubernetes depth** — storage, networking, policies, probes, resource requests/limits, and failure-diagnosis practices applied to the running fleet.
- **GitOps delivery with Argo CD** — reconciling the cluster's desired state from Git.
- **CI/CD with GitHub Actions** — build, test, and delivery pipelines authenticating to Azure with OIDC federated credentials and no stored secrets — [ADR-022](adr/ADR-022-cicd-github-actions-oidc.md).
- **Infrastructure provisioning and teardown pipelines** — automated apply and destroy of the environment as code.
- **Observability** — OpenTelemetry instrumentation, Prometheus metrics, and Grafana dashboards, with Azure Monitor / Application Insights as the logging destination — [Observability design](design/OBSERVABILITY.md).
- **Architecture and flow diagrams** — C4 plus Mermaid, including regeneration of the C4 model to match the current service set — [Architecture reference](architecture/C4Architecture.md).
- **Concept deep-dive library** — a reference covering each pattern and Azure service in use, with rationale and trade-offs.
- **Full rebuild runbook** — taking an operator from an empty subscription to the running platform.
- **Development and test guides, and a navigable documentation index** — consolidating the procedures and cross-links — [Testing index](test/README.md).

## Planned — future

- **Hardened container base image** — a chiseled/distroless .NET base image published to ACR and consumed by all service images, reducing the attack surface and centralising runtime patching in one place — [ADR-018](adr/ADR-018-aks-workload-identity-base-image.md) (recorded as future work).
- **Security programme** — a cross-cutting programme across identity, network, runtime, supply chain, data, detection, and governance: DAST, Kubernetes network policies, pod-security admission, dependency and image scanning, cloud workload protection, policy enforcement, and audit logging with alerting; with image signing, customer-managed keys, secret rotation, and threat modelling documented.
- **ISO/IEC 27001 alignment** — implement the applicable controls and document the mapping from control to implementation.
- **Sovereign and regulated-cloud considerations** — data residency, regional restrictions, and deployment constraints, documented for regulated-environment readiness.
- **Cost management** — cost-optimisation practices documented and applied where practical, including right-sizing, idle-resource management, and cost visibility.
- **Performance and scalability validation** — load and performance testing with horizontal pod autoscaling and cluster autoscaling, producing documented evidence that the platform scales.
- **Multi-cloud delivery** — deploying the same application codebase to AWS through a separate infrastructure and delivery pipeline, demonstrating portability of the application layer.
- **Service mesh with mutual TLS** — a mesh providing mTLS between services for authenticated, encrypted in-cluster traffic.
- **Shared building blocks as a package feed** — publishing the cross-cutting library as a versioned package for consumption across services.

## Conventions and key facts

| Item | Value |
|------|-------|
| Azure resource naming | `antkart-` prefix (resource groups, registry, Key Vault, Service Bus, and so on) |
| In-cluster naming | `ak-` prefix for Kubernetes Services and ServiceAccounts (`ak-products`, `ak-cart`, `ak-order`, `ak-payments`, `ak-discount`, `ak-gateway`) |
| Kubernetes namespace | `antkart` |
| Container registry | `acrantkartdev.azurecr.io`, images under the `antkart/<service>` namespace |
| Workload-identity federated subject | `system:serviceaccount:antkart:ak-<service>` (exact-match, case-sensitive); audience `api://AzureADTokenExchange` |
| Operational detail | [AKS Guide](guides/aks-guide.md) (cluster, operator access, workload identity, troubleshooting) · [Container Configuration](guides/container-configuration.md) (per-service runtime keys) · [infrastructure/README](../infrastructure/README.md) (module and apply-order map) |

## Maintenance note

This document is updated as items move between **Delivered**, **In progress**, and **Planned**. It stays a summary with links: detailed decisions belong in the [Architecture Decision Records](adr/README.md), and detailed procedures belong in the [guides](guides/). When a claim here and a guide disagree, the guide and the code are authoritative and this document is corrected to match.
