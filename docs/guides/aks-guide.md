# AKS Guide

**Purpose:** How the services are containerized and run on a managed Kubernetes (AKS) cluster — packaging, image delivery, ingress, health management, and workload-identity-based access to cloud resources with no stored secrets.

This guide is built one area at a time, following the same rhythm as the other build guides. **Containerization is complete and is documented below.** The cluster, Helm packaging, ingress/TLS, in-cluster workload identity, and GitOps delivery are marked as placeholders and are written as they are delivered. For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md); for the decisions behind the cluster shape, see [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

---

## Container Strategy

Every deployable service is packaged as a container image built the same way, so the fleet is uniform to build, scan, and run.

**Multi-stage builds.** Each `Dockerfile` uses a multi-stage build: a .NET 9 **SDK** stage restores and publishes the service, and a smaller .NET 9 **ASP.NET runtime** stage carries only the published output. Restore is performed before the full source is copied so Docker's layer cache is reused across builds when only source — not project references — changes. The build context is the **repository root** (the shared `AK.BuildingBlocks` project and `nuget.config` live above each service folder), so images are built with `-f <service>/Dockerfile .` from the root.

**Non-root, on port 8080.** Each runtime image sets `USER $APP_UID` (the .NET base image's non-root user) before the entry point, so containers never run as root. Because a non-root user cannot bind privileged ports (below 1024), every service listens on **port 8080** — the .NET base image's default (`ASPNETCORE_HTTP_PORTS=8080`) — and each `Dockerfile` declares `EXPOSE 8080`. Kubernetes Services target port 8080.

**Health and readiness endpoints.** Every service exposes the three health surfaces provided by the shared building blocks, shaped for orchestrator probes (see the [Observability design](../design/OBSERVABILITY.md) and the resilience/health step of the [Cloud Migration Guide](cloud-migration-guide.md)):

- **`GET /health/live`** — shallow liveness. Makes no external calls, so a dependency blip cannot trigger a restart storm. This is the **liveness probe** target.
- **`GET /health/ready`** — tolerant readiness (a degraded shared dependency still returns HTTP 200). This is the **readiness probe** target.
- `GET /health/deps` — detailed per-dependency diagnostics for humans and dashboards; **not** a probe target.

The Kubernetes probe definitions that consume `/health/live` and `/health/ready` are configured with the Helm packaging (a placeholder below).

**Serverless notification is not containerized.** `AK.Notification.Functions` is deployed as an Azure Function (Event Grid-triggered) and is **not** part of the container fleet — it is delivered through the serverless path, not the cluster.

### The `.dockerignore` rationale

A repository-root `.dockerignore` keeps the build context small and clean. It matters for two reasons:

- **Build speed and cache stability.** With the build context at the repository root, everything not excluded is sent to the Docker daemon on every build. Excluding build output (`bin/`, `obj/`), IDE state, `node_modules`, and test artifacts keeps the context small so builds start quickly and caching stays effective.
- **Keeping local configuration and secrets out of images.** Development-only configuration (`appsettings.Development.json`), local user files, and any local secret material must never be copied into an image layer — image layers are inspectable and are pushed to a registry. Runtime configuration is supplied from environment variables and Key Vault instead (see [Container Configuration](container-configuration.md)), never baked in at build time.

---

## Build and Push to the Azure Container Registry

Images are pushed to the platform's Azure Container Registry (see the [Infrastructure Guide](infrastructure-guide.md)). Access is by Microsoft Entra identity — the registry has its admin account disabled — so authentication uses the signed-in identity, with **no registry username or password**.

```bash
# Authenticate to the registry with your Entra identity (no registry credentials).
az acr login --name acrantkartdev

# Build and push one service (build context is the repository root).
docker build -f AK.Products/AK.Products.API/Dockerfile \
  -t acrantkartdev.azurecr.io/antkart/products:<tag> .
docker push acrantkartdev.azurecr.io/antkart/products:<tag>
```

Alternatively, `az acr build -r acrantkartdev -t antkart/products:<tag> -f AK.Products/AK.Products.API/Dockerfile .` builds server-side in the registry and pushes in one step.

At run time the cluster pulls images with **no registry credentials**: the cluster's kubelet identity is granted the built-in **AcrPull** role on the registry, so image pulls are authorized by managed identity. That role assignment is provisioned with the cluster (see [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md)).

### Image naming and tagging convention

Images live under the `antkart/` namespace in the registry, one repository per deployable service:

`acrantkartdev.azurecr.io/antkart/<service>:<tag>`

Each image is tagged with the **short Git commit SHA** of the build for immutable, traceable provenance, plus a moving environment tag (for example `dev`) that points at the latest image promoted to that environment. Deployments reference the immutable SHA tag so a rollout is always tied to an exact commit.

---

## Naming Conventions: In-Cluster Services vs Azure Resources

Two naming schemes are used deliberately, and they are not interchangeable:

- **In-cluster Kubernetes Service names use the short `ak-` prefix.** These are the DNS names services use to reach each other inside the cluster (for example, the gateway's downstream routes and the Order service's catalogue calls resolve to these).
- **Azure resources keep the `antkart-` prefix** (resource groups, registry, Key Vault, Service Bus, and so on), consistent with the infrastructure-as-code naming.

The complete mapping of service, `Dockerfile`, image repository, and in-cluster Service name:

| Service | Dockerfile | Image repository | In-cluster Service |
|---------|------------|------------------|--------------------|
| Products (REST) | `AK.Products/AK.Products.API/Dockerfile` | `antkart/products` | `ak-products` |
| ShoppingCart (REST) | `AK.ShoppingCart/AK.ShoppingCart.API/Dockerfile` | `antkart/cart` | `ak-cart` |
| Order (REST) | `AK.Order/AK.Order.API/Dockerfile` | `antkart/order` | `ak-order` |
| Payments (REST) | `AK.Payments/AK.Payments.API/Dockerfile` | `antkart/payments` | `ak-payments` |
| Gateway | `AK.Gateway/AK.Gateway.API/Dockerfile` | `antkart/gateway` | `ak-gateway` |
| Discount (gRPC) | `AK.Discount/AK.Discount.Grpc/Dockerfile` | `antkart/discount` | `ak-discount` |

The runtime configuration each of these services requires — connection strings, endpoints, and the values that must come from environment or Key Vault rather than committed configuration — is catalogued in [Container Configuration](container-configuration.md), which is the source for the Helm values.

---

## Still to Come

The sections below cover work that is **not yet delivered**. They are placeholders describing scope only; they will be written as each area is built, and nothing here should be treated as done.

### 🚧 The AKS cluster _(placeholder — not yet provisioned)_

Provisioning the managed Kubernetes cluster: node pools, networking plugin, SKU tier, health/observability integration, and the kubelet AcrPull grant. The decisions are recorded in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md); the cluster itself is not yet provisioned.

### 🚧 Base-image hardening _(placeholder)_

Moving the runtime image to a hardened, minimal base and publishing an organisation-owned base image, per the intent in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

### 🚧 Helm packaging _(placeholder)_

Packaging the services as Helm charts — Deployments, Services, probes wired to `/health/live` and `/health/ready`, resource requests/limits, and per-environment values sourced from [Container Configuration](container-configuration.md).

### 🚧 Ingress and TLS _(placeholder)_

External access to the cluster: the ingress controller, routing to the gateway, and TLS termination.

### 🚧 Workload identity _(placeholder)_

Secret-less access to Azure resources from inside the cluster: federating each service's Kubernetes ServiceAccount to an Entra identity so pods obtain Entra tokens with no stored secret, using the same `DefaultAzureCredential` code path the services already run locally.

### 🚧 GitOps delivery _(placeholder)_

Continuous delivery of the cluster's desired state from Git. The CI/CD platform and its authentication model are decided in [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md); the delivery pipeline is documented in the [DevOps Guide](devops-guide.md) as it is built.
