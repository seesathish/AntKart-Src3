# Infrastructure Guide — Terraform & Terragrunt

## Purpose

This guide explains how the platform's cloud infrastructure is provisioned with **Terraform** and **Terragrunt** — the concepts behind each resource, the scripts that define it (explained in detail), the commands that execute it, and the checks that verify it exists and is configured correctly.

It is written to serve two audiences at once: as a **build reference** for provisioning the environment, and as a **learning resource** for understanding what each resource is and why it is configured the way it is. Every infrastructure component is documented to the same depth and in the same shape, so the guide reads consistently from start to finish.

---

## How to Use This Guide

Every infrastructure component below follows the same **four-part rhythm**:

1. **Understand** — the concept. What the resource is, the problem it solves, and how it fits into the wider architecture.
2. **Build** — how the Terraform/Terragrunt is written. The configuration is shown with heavy inline comments and explained block by block, so the *why* behind each setting is clear, not just the *what*.
3. **Execute** — the commands that provision it (`terraform` / `terragrunt` plan and apply), including the order of operations and any dependencies on earlier components.
4. **Verify** — the checks that confirm the resource exists and is correct, using the Azure CLI and the portal, with the expected output described.

Read a section top to bottom to learn a resource end to end; jump straight to **Execute** and **Verify** when you are provisioning or validating.

---

## Prerequisites & Authentication Model

Before provisioning anything, it is important to understand *who* Terraform acts as and *what* it is allowed to do.

- **Identity tenant vs. subscription.** Identities (users, groups, service principals, app registrations) live in the **identity tenant**; billable resources live in a **subscription**. The two are distinct: the tenant answers "who are you", the subscription answers "where do resources live and who pays". Terraform needs context in both.
- **The service principal Terraform authenticates as.** Terraform does not run as a human. It authenticates as a dedicated **service principal** — a non-interactive identity with its own credentials — so that automation is repeatable and auditable, and so the same configuration runs identically on a developer machine and in a pipeline.
- **Authorization via RBAC roles.** Authenticating proves *identity*; it does not grant *permission*. The service principal is assigned **role-based access control (RBAC)** roles at the subscription scope. The automation identity holds **exactly two roles** — **Contributor** to create and manage resources, and **Role Based Access Control Administrator** to manage the role assignments the platform provisions for its own managed identities — and nothing wider (least privilege). The rationale is in Step 1.
- **Remote state with locking.** Terraform records what it has created in **state**. State is stored **remotely** (not on a single machine) so the team shares one source of truth, and it is protected by **locking** so two runs cannot modify the same infrastructure concurrently and corrupt it.

> The Terraform service principal and its RBAC assignment are created in **Step 1** below — with the Azure CLI, because the automation identity must exist before Terraform can authenticate as it. The remote state backend and Terragrunt root are bootstrapped in **Step 2**.

---

## Repository Hygiene

Terraform and Terragrunt generate local working files — the `.terraform/` provider directory, the `.terragrunt-cache/`, and `*.tfstate` files — that are transient or environment-specific and are **gitignored**; they are never committed. The one deliberate exception is the **provider lock file** (`.terraform.lock.hcl`), which **is committed**: it pins the exact provider versions so every run — on any machine or in a pipeline — resolves the same providers, the infrastructure equivalent of pinned package versions and the basis for reproducible builds. State itself lives only in the remote Azure backend (Step 2), never in the repository.

---

## Infrastructure Components

Each component below is documented with the same four-part structure. Sections are filled in as the component is built, capturing the real configuration, the real commands, and the real verification output.

### 1. Terraform Identity & Access (Service Principal, RBAC)

> This step uses the **Azure CLI, not Terraform** — the automation identity must exist before Terraform can authenticate as it, so there is no `.tf` script here, only documented commands.

#### Understand

**Tenant (directory) vs. subscription.** A **tenant** is the *identity* boundary — it holds users, groups, service principals, and app registrations. A **subscription** is the *billing and resource* boundary — it holds and pays for the resources you create. One tenant can contain **many** subscriptions; each subscription belongs to **exactly one** tenant. A single user can belong to **many** tenants. In short: the tenant answers "who are you", the subscription answers "where do resources live and who pays".

**Authentication vs. authorization.** These are two separate things:
- **Authentication** proves *who* an identity is. A **service principal** is a non-human identity (the automation equivalent of a user account) used to authenticate with no person present.
- **Authorization** decides *what* that identity may do. It is granted by an **RBAC role assignment**, which binds a role (a set of permissions) to an identity at a scope. Authenticating successfully grants nothing on its own — a role assignment is what confers permission.

**Why a service principal instead of a personal login.**
- **Non-interactive** — automation cannot answer an MFA prompt; a service principal authenticates with credentials, no human interaction.
- **Least privilege & bounded blast radius** — it carries only the permissions it needs, and if its secret leaks you rotate one secret rather than exposing a person's full access.
- **Auditability** — automated actions are attributable to the automation identity, kept distinct from human activity in the logs.
- **Clean lifecycle** — it can be created, scoped, rotated, and deleted independently of any individual's account.

**Why these two roles — and only these two.** The automation identity holds exactly two roles, each for a distinct, deliberate reason:
- **Contributor** — to create, read, update, and delete resources (everything an IaC identity needs to build the environment). Contributor deliberately **cannot grant access to others** — it cannot create role assignments — which is precisely why a second role is required.
- **Role Based Access Control Administrator** — to create and manage **role assignments**. The platform provisions its own RBAC grants as part of the infrastructure: managed identities are granted access to the Key Vault, the messaging namespace, and the container registry. Building those grants requires an identity that can manage role assignments.

This is a **least-privilege design choice**. The ability to manage role assignments could also come from the broader **User Access Administrator** role, but **Role Based Access Control Administrator** is the narrower, purpose-built role for exactly that task. Both can manage role assignments; choosing the narrower one gives the identity precisely the two capabilities the platform needs — **resource management** and **RBAC management** — and nothing wider. The assignment required no additional condition.

**How Terraform consumes this.** The `azurerm` provider authenticates as the service principal using four environment variables — `ARM_CLIENT_ID` (the SP's application id), `ARM_CLIENT_SECRET` (its secret), `ARM_TENANT_ID` (the directory), and `ARM_SUBSCRIPTION_ID` (where resources are created). When these are present, Terraform runs as the service principal with its assigned permissions, with no `az login` and no interactive sign-in.

#### Build

This step has **no Terraform script**. There is a bootstrapping order: you cannot use Terraform to create the very identity Terraform signs in with, so the service principal and its two role assignments are created once, up front, with the **Azure CLI**.

The only outputs that matter for later steps are the four `ARM_*` values. The **client secret is sensitive** — it is never written to a `.tf` file, a variables file, or anything committed to the repository. It is supplied to Terraform only through environment variables (and later through a secret store or pipeline secret).

#### Execute

Run from PowerShell, signed in to the correct tenant (`az login`) with the target subscription selected. Replace `<subscription-id>` (and the captured `<appId>` / `<password>` / `<tenant>`) with real values.

```powershell
# 0. Select the target subscription (the resource home and the role-assignment scope)
az account set --subscription "<subscription-id>"

# 1. Create the service principal with Contributor scoped to the subscription.
#    This prints appId, password, and tenant exactly once — capture them now.
az ad sp create-for-rbac `
  --name "antkart-terraform-sp" `
  --role "Contributor" `
  --scopes "/subscriptions/<subscription-id>"

# 2. Assign the second role so the identity can manage the RBAC grants the
#    platform provisions (e.g. managed identities -> Key Vault, Service Bus,
#    container registry). No additional condition is required.
az role assignment create `
  --assignee "<appId>" `
  --role "Role Based Access Control Administrator" `
  --scope "/subscriptions/<subscription-id>"

# 3. Set the four ARM_* environment variables for this session so Terraform
#    authenticates as the service principal. Use the values printed in step 1.
$env:ARM_CLIENT_ID       = "<appId>"
$env:ARM_CLIENT_SECRET   = "<password>"
$env:ARM_TENANT_ID       = "<tenant>"
$env:ARM_SUBSCRIPTION_ID = "<subscription-id>"
```

What each does:
- **Step 0** — sets the active subscription so the service principal and its role assignments land in the right place.
- **Step 1** — creates the `antkart-terraform-sp` service principal and assigns it **Contributor** at subscription scope; prints the credentials once.
- **Step 2** — assigns **Role Based Access Control Administrator** at subscription scope so the identity can create and manage role assignments (with no additional condition).
- **Step 3** — exports the four `ARM_*` variables so the `azurerm` provider authenticates as the service principal for the rest of the session.

`az ad sp create-for-rbac` prints the password only once. If it is lost, reset it (see the security note) rather than recreating the service principal.

#### Verify

```powershell
# Confirm both role assignments at the subscription scope.
az role assignment list --assignee "<appId>" -o table
```

A **correct result** shows **two rows**, both with **Principal** = the SP's `appId` and **Scope** = `/subscriptions/<subscription-id>`: one with **Role** = `Contributor` and one with **Role** = `Role Based Access Control Administrator`.

In the portal:
- **Microsoft Entra ID → App registrations** → search `antkart-terraform-sp` to see the identity.
- **Subscription → Access control (IAM) → Role assignments** → filter by the SP name → it appears with both the **Contributor** and **Role Based Access Control Administrator** roles at subscription scope.

Optional end-to-end check once the variables are set: a later Terraform `plan` that authenticates without prompting confirms the credentials and permissions are wired correctly.

> **Security note.** The client secret (`ARM_CLIENT_SECRET`) is a credential equivalent to a password. Store it only in environment variables now, and in a secret store or pipeline secret later — **never** in the repository, a `.tf` file, or a committed variables file. Secrets can and should be rotated: `az ad sp credential reset --id <appId>` issues a new secret and invalidates the old one, without recreating the service principal or its role assignment.

### 2. Remote State Backend & Terragrunt Root

> This step bootstraps the state backend with the **Azure CLI** (the storage must exist before Terraform can write state to it) and adds the first **Terragrunt** configuration under `infrastructure/`.

#### Understand

**What Terraform state is.** Terraform keeps a **state file** — the source-of-truth mapping between the resources declared in code and the real resources that exist in Azure. It records each resource's identity and attributes so Terraform knows what already exists, what to change, and what to destroy. Without state, Terraform cannot tell "create this" apart from "this already exists".

**Why state must be remote.** By default, state is a local file, which is fragile: it lives on one machine, is easily lost, and cannot be shared. Storing it **remotely** (in Azure Storage) makes it **shareable** across developer machines and pipelines — everyone reads and writes one authoritative copy — and **durable**, protected by the storage account's redundancy rather than a single laptop.

**Why state must be locked.** If two `apply` operations run against the same state at once, they can interleave writes and corrupt it. The backend therefore **locks** the state for the duration of an operation, **serializing** applies so only one runs at a time; a second is refused until the first releases the lock. Locking — together with a single shared remote state — also prevents **configuration drift**: there is one source of truth that everyone plans and applies against, so the code and the real infrastructure stay aligned.

**The Terragrunt root (`root.hcl`).** Rather than repeat the backend configuration in every module, the platform configures it **once** in a Terragrunt root file. Every module includes this root, so they all inherit the same backend — the setup is **DRY**, and because it lives in exactly one place it cannot drift between modules. The root also generates a shared provider so every module authenticates the same way.

#### Build

This step adds two things under `infrastructure/`:

- **`infrastructure/environments/dev/root.hcl`** — the Terragrunt root. Its `remote_state` block declares the **`azurerm`** backend and points it at a dedicated state **resource group**, **storage account**, and blob **container**. The `generate` directive writes a `backend.tf` into each child module at init time, so every module inherits the backend without copy-paste. The `key` is derived per module (`${path_relative_to_include()}/terraform.tfstate`), giving each module its own isolated state blob so two modules can never write to the same state. `use_azuread_auth = true` means the backend authenticates to the state storage with the caller's **Azure AD (Entra) identity** — the Step 1 service principal — **rather than a storage account access key**. This keeps the state backend consistent with the platform's secret-less, Entra-based security model and avoids storage account keys entirely: the storage account has **shared-key access disabled** (so Azure AD is the only supported path), and the service principal is granted the **Storage Blob Data Contributor** role on the state account to read and write the state blobs — **no storage key and no secret in the repo**. A `generate "provider"` block writes a shared `azurerm` provider into each module, reading the `ARM_*` variables to authenticate. State **locking** is built into the `azurerm` backend via a **blob lease**, and the storage account encrypts state **at rest** by default.
- **`infrastructure/modules/`** — established now (empty) as the home for the reusable resource modules added from Step 3 onward.

The `root.hcl` is heavily commented and explained block by block in the file itself; the key blocks are `locals` (the backend coordinates), `remote_state` (backend + `generate "backend.tf"` + per-module `key` + Entra auth), `generate "provider"` (the shared provider), and `inputs` (environment-wide values).

#### Execute

State backends are bootstrapped with the **Azure CLI**, because the storage account must exist before Terraform/Terragrunt can write state to it — the same bootstrapping order as the Step 1 identity. Run from PowerShell, signed in with `az login` (and `ARM_*` set from Step 1).

**(a) Create the state backend — resource group, storage account, data-plane role, container:**

```powershell
# Names — keep the storage account name globally unique, lowercase, 3-24 chars
$STATE_RG        = "rg-antkart-tfstate"
$STATE_SA        = "stantkarttfstate"   # change if this name is already taken
$STATE_CONTAINER = "tfstate"
$LOCATION        = "eastus"

# 1. Dedicated resource group for Terraform state (kept apart from app resources)
az group create --name $STATE_RG --location $LOCATION

# 2. Storage account: locally redundant, TLS 1.2+, no public blob access, and
#    shared-key access disabled so ALL access is via Entra identity (no keys).
az storage account create `
  --name $STATE_SA `
  --resource-group $STATE_RG `
  --location $LOCATION `
  --sku Standard_LRS `
  --kind StorageV2 `
  --min-tls-version TLS1_2 `
  --allow-blob-public-access false `
  --allow-shared-key-access false

# 3. Grant the Terraform service principal DATA-plane access to the state.
#    Control-plane Contributor does NOT include blob data access, so the
#    identity that reads/writes state needs Storage Blob Data Contributor.
$SA_ID = az storage account show --name $STATE_SA --resource-group $STATE_RG --query id -o tsv
az role assignment create `
  --assignee "<terraform-sp-appId>" `
  --role "Storage Blob Data Contributor" `
  --scope $SA_ID

# 4. Blob container that will hold the state files (created via Entra auth)
az storage container create `
  --name $STATE_CONTAINER `
  --account-name $STATE_SA `
  --auth-mode login
```

**(b) Initialize Terragrunt.** `root.hcl` is included by child modules; it is not applied on its own. Terragrunt initializes the backend the first time a module that includes it is run — the **Resource Group** in Step 3. On that first init, Terragrunt generates `backend.tf` and `provider.tf` in the module and connects it to the remote state:

```powershell
# Run from a module directory that includes the root (the first is Step 3).
cd infrastructure/environments/dev/<module>
terragrunt init     # wires up the backend; the first apply then creates the state blob
```

> From `infrastructure/environments/dev`, `terragrunt run-all init` initializes every module at once, once modules exist.

What each does:
- **`az group create`** — creates the dedicated resource group that holds the state storage account.
- **`az storage account create`** — creates the state storage account with key access disabled (Entra-only), TLS 1.2+, and no public blob access.
- **`az role assignment create` (Storage Blob Data Contributor)** — grants the Terraform identity data-plane access so it can read/write state blobs (Contributor alone cannot).
- **`az storage container create`** — creates the `tfstate` container that holds the per-module state blobs.
- **`terragrunt init`** — generates the backend/provider into a module and connects it to the remote state (run from Step 3 onward).

#### Verify

```powershell
# State storage account exists and is provisioned
az storage account show --name $STATE_SA --resource-group $STATE_RG -o table

# State container exists (Entra-auth data-plane call)
az storage container show --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table
```

A **correct result** shows the storage account with `provisioningState = Succeeded` and `kind = StorageV2`, and the container reported as present.

Confirm the account is hardened for Azure AD-only access:

```powershell
az storage account show --name $STATE_SA --resource-group $STATE_RG `
  --query "{minTls:minimumTlsVersion, sharedKey:allowSharedKeyAccess}" -o table
```

A **correct result** shows `minimumTlsVersion = TLS1_2` and `allowSharedKeyAccess = False` — confirming the backend is reachable only over TLS 1.2+ and only via Azure AD (no storage account keys).

**State locking** is active automatically: while a `terragrunt apply` runs, the backend holds a **lease** on the state blob, and a second concurrent apply is refused until it is released. You can observe the lease during an apply:

```powershell
az storage blob show `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --name "<module>/terraform.tfstate" --auth-mode login `
  --query "properties.lease" -o json
# during an apply: leaseState = "leased"
```

**The state blob appears after the first apply.** Before Step 3 the container is empty; after the first module's `terragrunt apply`, a state blob appears at the module's key:

```powershell
az storage blob list `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --auth-mode login -o table
# e.g. resource-group/terraform.tfstate
```

### 3. Resource Group

> This step adds the **first Terraform module** (`modules/resource-group`) and the **first Terragrunt live unit** (`environments/dev/resource-group`) — the first thing actually provisioned.

#### Understand

**What an Azure Resource Group is.** A **resource group** is a logical container for related Azure resources. It is the unit of **lifecycle** (resources in a group are typically created and deleted together), **access control** (RBAC can be scoped to the group), **tagging**, and **cost grouping** (costs roll up by group). Almost every resource lives in exactly one resource group, so it is the natural first thing to create — the parent scope everything else is placed in.

**The module-vs-live split.** The platform separates *how* from *what*:
- The **reusable module** (`infrastructure/modules/resource-group`) defines **how** a resource group is built — its inputs, the resource, and its outputs — with **no hardcoded environment values**.
- The **environment (live) config** (`infrastructure/environments/dev/resource-group`) supplies **what** — this environment's name, location, and tags — and **reuses** the module via Terragrunt.

This keeps the setup **DRY** (the resource is defined once) and ensures environments are **structurally identical** — dev, and any future environment, run the same module and differ only in their inputs. Every component from here on follows this split.

**`prevent_destroy`.** A resource group is a **stateful container** that holds every resource in the environment, so the module sets `lifecycle { prevent_destroy = true }` on it: Terraform will refuse any plan that would delete the resource group until the guard is intentionally removed, protecting against an accidental teardown that would take everything with it.

#### Build

This step adds the first **module** and its first **live unit**:

- **`infrastructure/modules/resource-group/`** — the reusable module:
  - **`variables.tf`** — the inputs: `name`, `location`, and `tags`, each with a description. `name` and `location` are required (no defaults); `tags` defaults to an empty map. No environment values are baked in.
  - **`main.tf`** — a single `azurerm_resource_group` built entirely from those inputs, with `lifecycle { prevent_destroy = true }`.
  - **`outputs.tf`** — exports `name`, `id`, and `location`, which later modules consume as their parent scope.
- **`infrastructure/environments/dev/resource-group/terragrunt.hcl`** — the live unit:
  - `include "root"` pulls in `root.hcl` (the shared Azure AD backend + provider).
  - `terraform { source = "../../../modules/resource-group" }` points at the module.
  - `inputs { ... }` supplies dev's values: `name = "rg-antkart-dev-eastus"`, `location = "eastus"`, and `tags` (`environment`, `project`, `managed-by`).

All files are heavily commented and explained inline.

#### Execute

> **Heads-up — a resource group named `rg-antkart-dev-eastus` already exists** (created manually in earlier work). Terraform must **create and own** what it manages, so before applying you must resolve this so Terraform is not fighting a resource it did not create. Choose one:
>
> - **(a) Recommended — delete the pre-existing RG for a clean start.** It is empty, so removing it lets Terraform create and own it cleanly. Confirm it is empty first, then delete:
>   ```powershell
>   # Confirm the group is empty (expect: an empty list / no rows) BEFORE deleting
>   az resource list --resource-group "rg-antkart-dev-eastus" -o table
>   # Delete it — only after confirming it is empty
>   az group delete --name "rg-antkart-dev-eastus" --yes
>   ```
> - **(b) Alternative — import it into Terraform state** instead of deleting (use this only if it ever holds resources worth keeping):
>   ```powershell
>   terragrunt import azurerm_resource_group.this `
>     "/subscriptions/<subscription-id>/resourceGroups/rg-antkart-dev-eastus"
>   ```
>
> Decide before `apply`. With the empty group, option (a) is the clean choice. **Do not run anything until you have decided.**

Run from the live unit's folder:

```powershell
cd infrastructure/environments/dev/resource-group

# 1. First init — wires up the Azure AD state backend, generates backend.tf and
#    provider.tf from the root, downloads the provider, and on this first run
#    creates the state blob for this unit in the tfstate container.
terragrunt init

# 2. Plan — review what will change before applying.
terragrunt plan

# 3. Apply — create the resource group. Confirm when prompted.
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates `backend.tf`/`provider.tf` from the root, connects to the Azure AD remote state, and initializes the provider.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add, 0 to change, 0 to destroy** (the resource group).
- **`terragrunt apply`** — creates the resource group and writes this unit's state to the backend.

#### Verify

```powershell
# 1. The resource group exists with the expected location and tags
az group show --name "rg-antkart-dev-eastus" -o table

# 2. The state blob now appears in the tfstate container (Azure AD auth)
az storage blob list `
  --account-name $STATE_SA --container-name $STATE_CONTAINER `
  --auth-mode login -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az group show` reports the group with `Location = eastus`, `ProvisioningState = Succeeded`, and the tags applied.
- `az storage blob list` now shows a blob at **`resource-group/terraform.tfstate`** — confirming the remote state, locking, and Azure AD auth from Step 2 work end to end.

### 4. Networking (VNet, Subnets, NSGs)

> Background reading: [Networking & Kubernetes Concepts](networking-concepts.md) — IP addressing & CIDR, VNet/subnet/NSG, Kubernetes fundamentals, and the Azure CNI IP-sizing math behind how these subnets are sized.

#### Understand

A quick recap (full detail in the [Networking & Kubernetes Concepts](networking-concepts.md) primer): a **VNet** is the private address space; **subnets** are non-overlapping per-workload slices of it; **NSGs** are stateful firewalls attached to subnets.

This step provisions a VNet and three subnets, each with its own NSG:

| Network | Range | Addresses | Why this size |
|---------|-------|-----------|---------------|
| **VNet** | `10.0.0.0/16` | 65,536 | The private space every subnet is carved from, sized large so there is room to grow. |
| **AKS subnet** | `10.0.0.0/22` | 1,024 | The **large** one: with traditional Azure CNI, **each pod consumes a subnet IP and each node pre-reserves a block of them**, so the cluster needs hundreds of addresses plus upgrade/scale headroom. |
| **Private-endpoints subnet** | `10.0.4.0/24` | 256 | Holds private endpoints to managed services; each consumes one IP, so a few hundred is ample. |
| **Gateway / APIM subnet** | `10.0.5.0/27` | 32 | A small, dedicated slice for the edge/gateway. |

Every subnet range sits **inside the VNet's `/16`** and the ranges **must not overlap**: `10.0.0.0/22` covers the third octet `.0`–`.3`, `10.0.4.0/24` is `.4`, and `10.0.5.0/27` sits in `.5`. Each subnet gets its **own NSG** so traffic to the cluster, the endpoints, and the gateway is controlled independently.

#### Build

This step adds a reusable **networking module** and its **dev live unit**:

- **`infrastructure/modules/networking/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `vnet_name`, `vnet_address_space`, a structured `subnets` map (each entry: `address_prefixes`, plus optional `service_endpoints` and `private_endpoint_network_policies`), and `tags`. No environment values baked in.
  - **`main.tf`** — an `azurerm_virtual_network`; the subnets via `azurerm_subnet` using **`for_each` over the `subnets` map** (so adding a subnet is a data change, not new code); one `azurerm_network_security_group` per subnet with an explicit **deny-by-default baseline** (allow intra-VNet, allow Azure Load Balancer probes, deny all other inbound — NSGs are stateful, so return traffic needs no separate rule); and `azurerm_subnet_network_security_group_association` to attach each NSG to its subnet.
  - **`outputs.tf`** — `vnet_id`, `vnet_name`, and `subnet_ids` (a map of subnet name → id) that later modules (AKS, private endpoints, gateway) consume.
- **`infrastructure/environments/dev/networking/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** block pointing at the `../resource-group` unit; it consumes that unit's `name` and `location` **outputs** rather than hardcoding them, and Terragrunt uses the dependency to apply the resource group **first**.
  - `terraform { source = "../../../modules/networking" }`.
  - `inputs` with dev's values: VNet `10.0.0.0/16`; subnets `aks = 10.0.0.0/22`, `private-endpoints = 10.0.4.0/24` (with `private_endpoint_network_policies = "Disabled"`), `gateway = 10.0.5.0/27`; matching tags.

This module **depends on the Resource Group** from Step 3, but only through the **Terragrunt dependency / outputs** mechanism — never hardcoded values — so the wiring stays correct if the resource group changes.

#### Execute

Run from the networking unit's folder:

```powershell
cd infrastructure/environments/dev/networking

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the VNet, subnets, NSGs, and associations
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports the **VNet + 3 subnets + 3 NSGs + 3 NSG-subnet associations** being **added** (≈ 10 to add), **0 to destroy**.
- **`terragrunt apply`** — creates the resources and writes this unit's state to the backend.

> The resource group must already exist (Step 3). Terragrunt applies the dependency first; if it has not been applied yet, apply the `resource-group` unit before this one (or run `terragrunt run-all apply` from `environments/dev`).

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# VNet and its address space (expect 10.0.0.0/16)
az network vnet show --resource-group $RG --name "vnet-antkart-dev-eastus" `
  --query "{name:name, addressSpace:addressSpace.addressPrefixes}" -o json

# Subnets and their prefixes
az network vnet subnet list --resource-group $RG --vnet-name "vnet-antkart-dev-eastus" `
  --query "[].{name:name, prefix:addressPrefix}" -o table

# NSGs (expect one per subnet)
az network nsg list --resource-group $RG --query "[].name" -o table
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: N added, 0 changed, 0 destroyed.` (N = the VNet + 3 subnets + 3 NSGs + 3 associations).
- `az network vnet show` reports the address space `10.0.0.0/16`.
- `az network vnet subnet list` shows `aks` → `10.0.0.0/22`, `private-endpoints` → `10.0.4.0/24`, `gateway` → `10.0.5.0/27`.
- `az network nsg list` shows `nsg-aks`, `nsg-private-endpoints`, and `nsg-gateway`, each associated to its subnet.

### 5. Azure Container Registry

#### Understand

A **container registry** is a private repository for **container images** — the packaged, ready-to-run artifacts the platform's services are built into. Images are pushed to the registry and later pulled by whatever runs them (the Kubernetes cluster, in a later step).

The platform uses a **private Azure Container Registry (ACR)** rather than a public registry because:

- **Images stay inside the Azure tenant** — access is controlled through Azure AD (Microsoft Entra) identities and RBAC, not exposed publicly.
- **Proximity to the cluster** — pulls happen within Azure's network, so they are faster and more reliable than pulling across the public internet.
- **No public rate limits** — public registries throttle anonymous pulls; a private ACR does not.
- **Integrated authentication** — workloads authenticate with their Azure identity. The built-in **`AcrPull`** role grants pull access; AKS uses it in a later step.
- **Foundation for supply-chain security** — it is where image scanning and signing are layered on later.

**Tiers.** The **Basic** tier is used for dev. **Premium** (for production) adds **private endpoints**, **geo-replication**, and **content trust**.

> **Who pulls images?** When AKS is introduced, it is the cluster's **kubelet identity** — the identity the nodes use to pull images — that is granted `AcrPull`, **not** the cluster's control-plane identity. That role assignment is made with the AKS wiring, not in this step.

#### Build

This step adds a reusable **container-registry module** and its **dev live unit**:

- **`infrastructure/modules/container-registry/`**:
  - **`variables.tf`** — `resource_group_name`, `location`, `acr_name` (globally unique, alphanumeric, 5–50 chars), `sku` (default `"Basic"`), and `tags`. No environment values baked in.
  - **`main.tf`** — an `azurerm_container_registry` built from the inputs, with **`admin_enabled = false`**. Disabling the admin account removes the single static username/password and forces all access through Azure AD identities and RBAC — consistent with the platform's secret-less model. A comment notes that `AcrPull` for the AKS kubelet identity is granted in a later step, not here.
  - **`outputs.tf`** — `id`, `name`, and `login_server`, consumed by later steps (AKS scopes its `AcrPull` to the `id`; build/push uses the `login_server`).
- **`infrastructure/environments/dev/container-registry/terragrunt.hcl`**:
  - `include "root"` for the shared backend/provider.
  - a **`dependency "resource_group"`** consuming the resource group's `name` and `location` outputs (with `mock_outputs` for `init`/`plan`/`validate`, matching the networking unit's pattern).
  - `terraform { source = "../../../modules/container-registry" }`.
  - `inputs`: `acr_name = "acrantkartdev"`, `sku = "Basic"`, and matching tags.

Like the networking unit, this module **depends on the Resource Group** only through the Terragrunt dependency / outputs mechanism — never hardcoded.

#### Execute

Run from the ACR unit's folder:

```powershell
cd infrastructure/environments/dev/container-registry

# 1. Init — generates backend.tf/provider.tf and resolves the resource_group dependency
terragrunt init

# 2. Plan — review before applying
terragrunt plan

# 3. Apply — create the container registry
terragrunt apply
```

What each does:
- **`terragrunt init`** — generates the backend/provider, initializes the provider, and resolves the `resource_group` dependency.
- **`terragrunt plan`** — shows the intended changes; a correct plan reports **1 to add** (the registry), **0 to change, 0 to destroy**.
- **`terragrunt apply`** — creates the registry and writes this unit's state to the backend.

> **The ACR name must be globally unique** (it becomes `<name>.azurecr.io`). If `apply` fails because the name is taken, change `acr_name` in the unit's `inputs` and re-run.

#### Verify

```powershell
$RG = "rg-antkart-dev-eastus"

# Registry name, SKU, login server, and that the admin account is disabled
az acr show --resource-group $RG --name "acrantkartdev" `
  --query "{name:name, sku:sku.name, loginServer:loginServer, adminEnabled:adminUserEnabled}" -o json
```

A **correct result**:
- `terragrunt apply` ends with `Apply complete! Resources: 1 added, 0 changed, 0 destroyed.`
- `az acr show` reports `name = acrantkartdev`, `sku = Basic`, `loginServer = acrantkartdev.azurecr.io`, and **`adminEnabled = false`** — confirming access is via Azure AD, not the admin account.

### 6. Key Vault

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 7. Log Analytics & Application Insights

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 8. Cosmos DB

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 9. Service Bus

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 10. Event Grid

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 11. Function App (Hosting)

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 12. Entra ID App Registration & Roles

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 13. Managed Identities & Workload Identity Foundation

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 14. Governance, Tagging & Cost Controls

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._
