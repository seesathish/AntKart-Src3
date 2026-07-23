# AKS Guide

**Purpose:** How the services are containerized and run on a managed Kubernetes (AKS) cluster — packaging, image delivery, ingress, health management, and workload-identity-based access to cloud resources with no stored secrets.

This guide is built one area at a time, following the same rhythm as the other build guides. **Containerization, the AKS cluster, and per-service workload identity are complete and are documented below.** Helm packaging, ingress/TLS, and GitOps delivery are marked as placeholders and are written as they are delivered. For where this fits in the overall build, see the [Development Guide](../../DevelopmentGuide.md); for the decisions behind the cluster shape, see [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

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

## The AKS Cluster

The cluster `aks-antkart-dev` is provisioned by the `aks` Terraform module and its dev unit (see the [Infrastructure map](../../infrastructure/README.md)). Its shape is a deliberate set of dev-appropriate choices; the reasoning for each is recorded in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

| Aspect | As-built | Why |
|--------|----------|-----|
| Kubernetes version | `1.35`, pinned | A pinned version makes provisioning reproducible. AKS retires versions over time, so the value is chosen from those marked `KubernetesOfficial` (see [Troubleshooting](#troubleshooting)). |
| Control-plane SKU tier | `Free` | No uptime SLA but no control-plane cost — right for a disposable dev cluster. Production selects `Standard` for the financially-backed SLA. |
| Node pool | A single **system** pool, `2 × Standard_D2s_v3`, **autoscaling disabled** | One fixed-size pool keeps dev cost predictable and the setup simple. Production splits system/user pools and enables autoscaling. |
| Node subnet | The `aks` subnet, `10.0.0.0/22` | The large subnet the networking module sized for the cluster's nodes. |
| Networking | **Azure CNI Overlay** — plugin `azure`, mode `overlay`, policy `azure` | Overlay draws pod IPs from an overlay CIDR so pods do **not** consume VNet address space; network policy is enforced by the Azure CNI itself (no separate Calico add-on). |
| Pod / service CIDRs | pod `10.244.0.0/16`, service `10.245.0.0/16`, DNS `10.245.0.10` | Chosen not to overlap the VNet (`10.0.0.0/16`) or each other. |
| Control-plane identity | `SystemAssigned` | Azure manages its lifecycle with the cluster. The nodes get a separate kubelet identity. |
| Image pulls | Kubelet identity granted **AcrPull** on the registry | Nodes pull images with **no `imagePullSecret`** — verified by a successful pull with no registry credentials. |
| OIDC issuer + workload identity | **Enabled at creation** | Both are required for federated workload identity. Enabling them after creation forces a cluster update, so they are set at creation time. |
| Authorization | **Azure RBAC** for Kubernetes enabled; local accounts left enabled | `kubectl` access is granted with an Azure role (see [Operator Access](#operator-access)). Local accounts are intentionally left enabled for now so we cannot lock ourselves out. |
| Monitoring | OMS agent → existing Log Analytics workspace | Reuses the platform's existing workspace, with managed-identity auth. |

---

## Operator Access

Because the cluster uses **Azure RBAC for Kubernetes authorization**, being able to reach the API server is not enough — an **Azure role** is required to run `kubectl`, and the Entra-enabled cluster needs `kubelogin` to convert the kubeconfig to a token-based login.

**1. Grant yourself a cluster RBAC role** (scoped to the cluster, not the subscription):

```bash
CLUSTER_ID=$(az aks show -g rg-antkart-dev-eastus -n aks-antkart-dev --query id -o tsv)

az role assignment create \
  --assignee "<your-entra-object-id>" \
  --role "Azure Kubernetes Service RBAC Cluster Admin" \
  --scope "$CLUSTER_ID"
```

**2. Fetch credentials and convert them for Entra login:**

```bash
az aks get-credentials -g rg-antkart-dev-eastus -n aks-antkart-dev

az aks install-cli                          # installs kubectl and kubelogin
kubelogin convert-kubeconfig -l azurecli    # REQUIRED for an Entra-enabled cluster

kubectl get nodes                           # first call triggers an Entra sign-in
```

`kubelogin` is **required** here: without converting the kubeconfig, `kubectl` cannot obtain an Entra token for the cluster and every call fails to authenticate. Note that `az aks install-cli` updates your `PATH` — **open a new shell** so `kubectl` and `kubelogin` resolve.

---

## Workload Identity

Pods reach Azure resources (Key Vault, Service Bus, Event Grid) with **no stored secret**, using the same `DefaultAzureCredential` line the services already run locally. This is delivered by the `workload-identity` module and its dev unit (see the [Infrastructure map](../../infrastructure/README.md)).

**The model.** For each service there is a **user-assigned managed identity** (`id-ak-<service>-dev`) with a **federated identity credential** that trusts the cluster's OIDC issuer for exactly one Kubernetes ServiceAccount:

- **Subject:** `system:serviceaccount:antkart:ak-<service>` — this is **exact-match and case-sensitive**. A mismatch (wrong namespace, wrong ServiceAccount name, wrong case) fails as `AADSTS70021` / "workload options are not fully configured", not as a permission error.
- **Audience:** `api://AzureADTokenExchange` — the fixed Entra token-exchange audience the workload-identity webhook expects.

### Least-privilege role matrix

Each identity receives only the data-plane roles its service needs. Cosmos DB, PostgreSQL, and Redis are reached via **connection strings stored in Key Vault**, so access to them is granted transitively through **Key Vault Secrets User** rather than a data-plane role on those stores.

| Service (ServiceAccount) | Managed identity | Roles granted → scope |
|--------------------------|------------------|-----------------------|
| `ak-products` | `id-ak-products-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| `ak-cart` | `id-ak-cart-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace |
| `ak-order` | `id-ak-order-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| `ak-payments` | `id-ak-payments-dev` | Key Vault Secrets User → vault · Azure Service Bus Data Sender + Data Receiver → namespace · EventGrid Data Sender → topic |
| `ak-discount` | `id-ak-discount-dev` | Key Vault Secrets User → vault |
| `ak-gateway` | `id-ak-gateway-dev` | Key Vault Secrets User → vault |

### Pod wiring

Two pieces connect a pod to its identity — an **annotation on the ServiceAccount** carrying the identity's client id, and a **label on the pod template** opting the pod into the webhook:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: ak-products                     # matches the federated-credential subject
  namespace: antkart
  annotations:
    azure.workload.identity/client-id: "<client_id of id-ak-products-dev>"
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ak-products
  namespace: antkart
spec:
  template:
    metadata:
      labels:
        azure.workload.identity/use: "true"    # MUST be on the pod template, not only the Deployment
    spec:
      serviceAccountName: ak-products
      containers:
        - name: ak-products
          image: acrantkartdev.azurecr.io/antkart/products:<tag>
```

With the annotation and label present, the webhook injects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_FEDERATED_TOKEN_FILE`, and `AZURE_AUTHORITY_HOST` into the pod, and `DefaultAzureCredential` resolves to `WorkloadIdentityCredential`. **Application code is unchanged.** The client id for each service comes from the `workload-identity` module's `identities` output.

**Verified behaviour.** A pod deployed **without** this wiring crashed: `DefaultAzureCredential` fell back to IMDS and failed with "Multiple user assigned identities exist" (the node carries several identities, and IMDS cannot pick one). The **same image with the annotation and label** reached `Running` and read Key Vault successfully — deterministically selecting the one federated identity for that service, with no secret involved.

---

## Cost and Idle Management

The node pool bills **hourly** whenever the cluster is running. For idle periods, stop and start the cluster rather than destroying it:

```bash
az aks stop  -g rg-antkart-dev-eastus -n aks-antkart-dev   # stops node billing
az aks start -g rg-antkart-dev-eastus -n aks-antkart-dev
```

---

## Troubleshooting

Issues encountered provisioning and operating this cluster, and how to resolve them:

- **Kubernetes version unavailable / LTS-only.** AKS continually retires versions. A version offered **only** under `AKSLongTermSupport` requires an LTS-tier subscription and will otherwise be rejected. List what a region actually offers and pick one marked `KubernetesOfficial`:
  ```bash
  az aks get-versions --location eastus -o table
  ```
- **VM size unavailable, or the wrong CPU architecture.** Subscription SKU allow-lists vary by region, so the intended VM size may be unavailable. Watch the **architecture**, not just availability: the available `b*ps_v2` sizes are **ARM64**, which will **not** run amd64-built images. Confirm both availability and architecture before choosing a size.
- **Provider lock drift across Terragrunt units.** Each Terragrunt unit pins its providers in its **own** `.terraform.lock.hcl`. Adding a provider to the shared root config does not update the units automatically — run `terragrunt init -upgrade` in each affected unit and commit the refreshed lock files.
- **`kubectl` cannot authenticate after `install-cli`.** `az aks install-cli` changes `PATH`; open a **new shell** so `kubectl`/`kubelogin` resolve, and ensure `kubelogin convert-kubeconfig -l azurecli` has been run for this Entra-enabled cluster.
- **Federated token rejected (`AADSTS70021`).** The ServiceAccount subject does not match the federated credential. The subject `system:serviceaccount:antkart:ak-<service>` is exact-match and case-sensitive — check the namespace, the ServiceAccount name, and the pod-template label.

---

## Still to Come

The sections below cover work that is **not yet delivered**. They are placeholders describing scope only; nothing here should be treated as done.

### 🚧 Base-image hardening _(placeholder)_

Moving the runtime image from the standard ASP.NET runtime to a hardened, minimal base and publishing an organisation-owned base image, per the intent in [ADR-018](../adr/ADR-018-aks-workload-identity-base-image.md).

### 🚧 Helm packaging _(placeholder)_

Packaging the services as Helm charts — Deployments, Services, probes wired to `/health/live` and `/health/ready`, resource requests/limits, the ServiceAccount annotations/labels above, and per-environment values sourced from [Container Configuration](container-configuration.md).

### 🚧 Ingress and TLS _(placeholder)_

External access to the cluster: the ingress controller, routing to the gateway, and TLS termination.

### 🚧 GitOps delivery _(placeholder)_

Continuous delivery of the cluster's desired state from Git. The CI/CD platform and its authentication model are decided in [ADR-022](../adr/ADR-022-cicd-github-actions-oidc.md); the delivery pipeline is documented in the [DevOps Guide](devops-guide.md) as it is built.
