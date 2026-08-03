# Heroic Runbook — Provisioning a New Environment

**Purpose.** Take an Azure subscription that already hosts the `dev` environment and stand up a
complete, independent second environment — infrastructure, cluster, identity, GitOps and CD —
without touching `dev` and without hand-editing anything twice.

**Audience.** Someone who has never provisioned this platform. Every step states what it does,
what to check before, and what proves it worked.

**Scope.** This runbook is written for `qa` but is **parameterized**. Set `$ENV` once and every
command follows. To build `stage`, `uat` or `prod` later, change that one value and re-run this
document from the top.

---

## 0. How to use this runbook

### 0.1 Set your environment variables — do this in every new shell

PowerShell loses variables when it resets. Re-run this block at the start of every session and
after any restart. Everything below depends on it.

```powershell
# ---- The only line you change per environment -------------------------------
$ENV = "qa"

# ---- Derived names (dev convention: <resource>-antkart-<env>) ----------------
$LOCATION      = "eastus"
$LOCATION_DATA = "eastus2"                       # see 1.4 — Postgres AND Redis
$RG            = "rg-antkart-$ENV-eastus"
$VNET          = "vnet-antkart-$ENV-eastus"
$AKS           = "aks-antkart-$ENV"
$ACR           = "acrantkart$ENV"                # globally unique, no hyphens
$KV            = "kv-antkart-$ENV"               # globally unique
$COSMOS        = "cosmos-antkart-$ENV"           # globally unique
$SB            = "sb-antkart-$ENV"               # globally unique
$EVGT          = "evgt-antkart-$ENV"
$REDIS         = "redis-antkart-$ENV"
$LOG           = "log-antkart-$ENV"
$APPI          = "appi-antkart-$ENV"
$PG            = "psql-antkart-$ENV-eus2"        # globally unique
$FUNC          = "func-antkart-notifications-$ENV"
$FUNC_SA       = "stantkartfunc$ENV"             # globally unique, no hyphens
$BUDGET        = "budget-antkart-$ENV"
$APPREG        = "antkart-api-$ENV"

# ---- State backend (shared account, per-environment container) ---------------
$STATE_RG        = "rg-antkart-tfstate"
$STATE_SA        = "stantkarttfstate"
$STATE_CONTAINER = "tfstate-$ENV"                # NOT "tfstate" — see Phase 0

# ---- Paths ------------------------------------------------------------------
$ENVDIR = "infrastructure/environments/$ENV"

Write-Host "Environment=$ENV  RG=$RG  StateContainer=$STATE_CONTAINER"
```

Confirm the printed line is what you expect before running anything else.

### 0.2 Session split

This is **two sessions**, not one. Say so to yourself now so the second half of day one is not a
disappointment.

| Session | Phases | Outcome |
|---|---|---|
| One | 0 – 4 | Infrastructure provisioned, cluster reachable, identity working, secrets vaulted |
| Two | 5 – 6 | GitOps, CD targeting the new environment, end-to-end verification |

### 0.3 Standing rules

- **Verify, never assume.** Every phase ends with a check. Run it even when you are confident.
- **Plan before apply.** `terragrunt plan` on every unit before the first `apply` of a wave.
- **Stop what you start.** AKS and PostgreSQL are the expensive resources. Phase 7 is teardown.
- **One wave at a time.** Do not run `run-all apply` across the whole tree on a first build.

### 0.5 The five ideas behind every command in this runbook

If Terraform is new to you, these five ideas explain most of what follows.

**1. Terraform describes; it does not script.** You declare the resources you want and
Terraform works out the calls to get there. Running the same file twice changes nothing
the second time — that property is called idempotence, and it is why re-running a failed
step is safe.

**2. State is the memory, and it is the dangerous part.** Terraform records what it
created in a state file. It compares that record against your files to decide what to
change. Point a new environment at another environment's state and Terraform concludes
the live resources are *its* resources — and offers to reshape them. That is the whole
reason Phase 0 exists.

**3. Terragrunt is a wrapper that removes repetition.** Terraform alone would need the
backend and provider settings copied into all 18 units. Terragrunt keeps them once in
`root.hcl` and generates the rest at `init` time. That is why `backend.tf` and
`provider.tf` are deleted in Phase 1 — they are generated files, not source.

**4. A "unit" is one folder, one state file.** Each folder under the environment is
applied independently and owns its own state blob. Small blast radius: a mistake in one
unit cannot corrupt another.

**5. `plan` shows, `apply` does.** Plan is a dry run printing what would change. It is
free and safe. Every apply in this runbook is preceded by a plan for one reason — the
plan is where you catch the wrong environment before it costs you.

### 0.6 The one-way doors

Three things in this runbook cannot be undone cheaply. They are called out where they occur.

| Door | Why it is one-way | Phase |
|---|---|---|
| Key Vault purge protection | Azure will not let it be disabled once enabled; a deleted vault's name is locked for the retention window | 1.3 |
| Terraform state container name | Changing it after apply orphans every state blob | 0 |
| Globally unique resource names | Taken names cannot be reused while soft-deleted | 1.2 |

---

## 1. Prerequisites and decisions

### 1.1 Prerequisite checks

Run all four. Do not proceed past a failure.

```powershell
# (a) Tooling present
az version
terraform version
terragrunt --version
kubectl version --client
helm version

# (b) Correct subscription selected
az account show --query "{name:name, id:id, tenant:tenantId}" -o table

# (c) The Terraform service principal still has its two subscription-scoped roles
#     (Contributor + Role Based Access Control Administrator). Replace the appId.
az role assignment list --assignee "<terraform-sp-appId>" `
  --query "[].{role:roleDefinitionName, scope:scope}" -o table

# (d) The four ARM_* variables are set for this shell
Get-ChildItem Env: | Where-Object { $_.Name -like "ARM_*" } | Select-Object Name
```

`(c)` must show **Contributor** and **Role Based Access Control Administrator** at subscription
scope. `(d)` must list `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, `ARM_SUBSCRIPTION_ID`,
`ARM_TENANT_ID`. If they are missing, re-set them — see
[Infrastructure Guide § Terraform Identity & Access](infrastructure-guide.md).

### 1.2 Check globally unique names before you create anything

Eight resource types take names from a global namespace. A name that is taken — or soft-deleted
elsewhere — fails the apply *midway through a wave*, which is the worst place to find out.

```powershell
# Storage accounts (function app storage)
az storage account check-name --name $FUNC_SA -o table

# Container registry
az acr check-name --name $ACR -o table

# Key Vault — a soft-deleted vault of the same name blocks creation
az keyvault list-deleted --query "[?name=='$KV'].{name:name, purgeDate:properties.scheduledPurgeDate}" -o table

# Service Bus namespace
az servicebus namespace exists --name $SB -o table

# Cosmos DB account name (empty output = available)
az cosmosdb check-name-exists --name $COSMOS

# Azure Managed Redis — name is globally unique (it forms the hostname)
az resource list --resource-type "Microsoft.Cache/redisEnterprise" `
  --query "[?name=='$REDIS'].{name:name, rg:resourceGroup}" -o table

# PostgreSQL flexible server — name is subscription+region unique; list existing
az postgres flexible-server list --query "[?name=='$PG'].name" -o tsv
```

**Correct result:** every check reports the name as available and `az keyvault list-deleted`
returns nothing for `$KV`.

If a Key Vault of this name is soft-deleted and purge protection was **off**, purge it:

```powershell
az keyvault purge --name $KV --location $LOCATION
```

If purge protection was **on**, the name is unusable until the retention window expires. Choose a
different `$ENV` suffix or a different vault name and record why in an ADR.

### 1.3 Decisions — dev conventions, with one deliberate deviation

| # | Decision | dev value | This environment | Rationale |
|---|---|---|---|---|
| A | Naming convention | `<resource>-antkart-dev` | `<resource>-antkart-$ENV` | Follow dev. Predictable, greppable. |
| B | Container registry | `acrantkartdev`, dedicated | **Dedicated** `acrantkart$ENV` | Follow dev. A shared ACR couples environments and complicates the AcrPull grant. |
| C | Key Vault purge protection | `true` | **`false`** | **Deviation — see below.** |
| D | Region | `eastus`, Postgres in `eastus2` | Same | Follow dev. Re-verify Postgres availability (1.4). |
| E | State isolation | container `tfstate` | container `tfstate-$ENV` | Option A. Dev's state key expression is never touched. |

**Decision C is the one place "follow dev" is wrong, and it is worth understanding.**

`dev` has `purge_protection_enabled = true` — not by design, but because it was switched on out of
band on the live vault and Azure will not allow turning it off. The module's own variable
description records the intent: *a genuinely disposable environment may set it false to allow early
purge and recreate during teardown.* That is exactly what a QA environment is.

With it `true`, tearing down and rebuilding this environment locks the vault name for the soft-delete
retention window — you cannot recreate it under the same name for up to seven days. That makes the
environment un-rebuildable, which defeats its purpose. This is tracked as **KI-007**.

The trade-off is real: production wants irreversibility, disposable environments want repeatability.
Record the choice as an ADR — *production irreversibility versus rebuild repeatability* — rather
than as a silent config difference.

Set it in `$ENVDIR/key-vault/terragrunt.hcl` (dev has it at line 65):

```hcl
purge_protection_enabled = false
```

### 1.4 Confirm the Postgres region workaround still applies

`dev` provisions PostgreSQL in `eastus2` because the required offer was restricted in `eastus`.
Check whether that is still true rather than copying the workaround blindly:

```powershell
az postgres flexible-server list-skus --location eastus -o table
```

If the SKU list returns normally, set `$LOCATION_DATA = "eastus"` and note the change. If it errors or
returns restricted offers, keep `eastus2`.

---

## Phase 0 — Isolate the state backend

**This is the highest-risk phase in the runbook and the shortest. Read 0.1 before running it.**

### Understand

Terragrunt derives each unit's state blob path from `path_relative_to_include()` — the unit's path
relative to the folder containing `root.hcl`. In this repo `root.hcl` lives **inside**
`infrastructure/environments/dev/`, so `dev/aks` resolves to `aks` and its state key is
`aks/terraform.tfstate`.

Copy that folder to `environments/qa/` and `root.hcl` comes with it. `qa/aks` **also** resolves to
`aks`, producing the identical key. Same container, same key, same blob. Terragrunt would attach the
new environment to dev's live state and Terraform would plan to recreate the running platform.

The fix is to change the **container**, not the key expression. Dev's 18 state blobs stay exactly
where they are at `<unit>/terraform.tfstate` in `tfstate`; the new environment gets its own
container with the same internal layout.

### Execute

```powershell
# Create the per-environment state container (Entra auth — shared keys are disabled)
az storage container create `
  --name $STATE_CONTAINER `
  --account-name $STATE_SA `
  --auth-mode login
```

### Verify

```powershell
# The new container exists
az storage container show --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table

# It is empty
az storage blob list --container-name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table

# CRITICAL: dev's 18 state blobs are untouched
az storage blob list --container-name "tfstate" --account-name $STATE_SA --auth-mode login `
  --query "length(@)" -o tsv
```

**Correct result:** the new container exists and is empty; the `tfstate` container still reports
**18** blobs. If that number is not 18, stop and investigate before going further.

---

## Phase 1 — Create the environment tree

### Understand

`infrastructure/modules/` is environment-agnostic — every `dev` mention there is inside a
`description` field, never a value. `infrastructure/environments/dev/` is the opposite: it is
*supposed* to hold environment-specific values, and it contains roughly sixty of them.

So this phase is a careful edit, not a rename. The list below is exhaustive as of commit `1cdfa11`.

### Execute — (a) copy the tree

```powershell
Copy-Item -Path "infrastructure/environments/dev" -Destination $ENVDIR -Recurse

# Remove any local Terraform working state that came along with the copy
Get-ChildItem -Path $ENVDIR -Recurse -Directory -Filter ".terraform" | Remove-Item -Recurse -Force
Get-ChildItem -Path $ENVDIR -Recurse -File -Include "backend.tf","provider.tf","versions.tf" | Remove-Item -Force
Get-ChildItem -Path $ENVDIR -Recurse -File -Filter "*.tfplan" | Remove-Item -Force
```

`backend.tf`, `provider.tf` and `versions.tf` are **generated** by Terragrunt from `root.hcl` at init
time. Deleting the copies guarantees they are regenerated against the new backend rather than
silently inherited.

### Execute — (b) the first edit, before anything else

Open `$ENVDIR/root.hcl` and change **line 31**:

```hcl
state_container = "tfstate-qa"     # was "tfstate"
```

Then **line 138**:

```hcl
inputs = {
  environment = "qa"               # was "dev"
}
```

Do this before any other edit. If you are interrupted, the environment is already safe.

### Execute — (c) the remaining edits

Find every remaining `dev` value:

```powershell
Get-ChildItem -Path $ENVDIR -Recurse -Filter "terragrunt.hcl" |
  Select-String -Pattern "dev" |
  Where-Object { $_.Line -notmatch "^\s*#" } |
  Select-Object Path, LineNumber, Line
```

Work through them by category:

| Category | Appears in | Change |
|---|---|---|
| Resource group name `rg-antkart-dev-eastus` | 13 units | `rg-antkart-$ENV-eastus` |
| Resource names | one per unit (see table below) | swap `dev` → `$ENV` |
| `environment = "dev"` tag | every unit, plus `workload-identity` line 166 | `"qa"` |
| Mock-output resource IDs | `aks`, `role-assignments`, `workload-identity`, `github-oidc`, `governance` | swap `dev` → `$ENV` in the paths |
| GitHub OIDC subject claim | `github-oidc` line 70 | `{ name = "env-qa", claim = "environment:qa" }` |
| App registration display name | `app-registration` line 27 | `antkart-api-$ENV` |
| Key Vault purge protection | `key-vault` line 65 | `false` (Decision C) |

Resource names by unit:

| Unit | dev value | New value |
|---|---|---|
| `resource-group` | `rg-antkart-dev-eastus` | `rg-antkart-$ENV-eastus` |
| `networking` | `vnet-antkart-dev-eastus` | `vnet-antkart-$ENV-eastus` |
| `aks` | `aks-antkart-dev` | `aks-antkart-$ENV` |
| `container-registry` | `acrantkartdev` | `acrantkart$ENV` |
| `key-vault` | `kv-antkart-dev` | `kv-antkart-$ENV` |
| `cosmosdb` | `cosmos-antkart-dev` | `cosmos-antkart-$ENV` |
| `servicebus` | `sb-antkart-dev` | `sb-antkart-$ENV` |
| `eventgrid` | `evgt-antkart-dev` | `evgt-antkart-$ENV` |
| `redis` | `redis-antkart-dev` | `redis-antkart-$ENV` |
| `observability` | `log-antkart-dev`, `appi-antkart-dev` | `log-antkart-$ENV`, `appi-antkart-$ENV` |
| `postgresql` | `psql-antkart-dev-eus2` | `psql-antkart-$ENV-eus2` |
| `function-app` | `func-antkart-notifications-dev`, `stantkartfuncdev` | `...-$ENV`, `stantkartfunc$ENV` |
| `communication-services` | `antkart-dev` prefix | `antkart-$ENV` |
| `governance` | `budget-antkart-dev` | `budget-antkart-$ENV` |
| `app-registration` | `antkart-api-dev` | `antkart-api-$ENV` |

> **Mock outputs.** The `00000000-0000-0000-0000-000000000000` subscription IDs are placeholders
> Terragrunt uses when a dependency has not been applied yet, so `plan` can run against an
> unbuilt graph. They are never real values — but the *resource names* inside them must still be
> updated, or a plan will validate against dev-shaped paths.
>
> A `dependency` block reads another unit's outputs. Terragrunt applies dependencies
> first and passes real values down; the mock values only stand in for a `plan` run
> before that unit exists.

### Verify

```powershell
# No unintended 'dev' values remain outside comments
Get-ChildItem -Path $ENVDIR -Recurse -Filter "terragrunt.hcl" |
  Select-String -Pattern "dev" |
  Where-Object { $_.Line -notmatch "^\s*#" }

# State container is correct — this is the one that matters
Select-String -Path "$ENVDIR/root.hcl" -Pattern "state_container|environment ="
```

**Correct result:** the first command returns nothing (or only intentional matches such as
`psql-...-eus2`, which contains no `dev`). The second shows `tfstate-qa` and `environment = "qa"`.

---

## Phase 2 — Waves 0 and 1 (foundation and standalone services)

### Understand

The 18 units form a dependency graph. Applying in wave order means every dependency's real outputs
exist before a dependent unit reads them.

| Wave | Units | Depends on |
|---|---|---|
| 0 | `resource-group`, `app-registration` | nothing |
| 1 | `networking`, `observability`, `container-registry`, `cosmosdb`, `postgresql`, `redis`, `servicebus`, `eventgrid`, `communication-services`, `governance` | `resource-group` |
| 2 | `aks`, `key-vault`, `github-oidc`, `function-app` | wave 0–1 |
| 3 | `workload-identity`, `role-assignments` | wave 2 |

### Execute — Wave 0

> `init` downloads the provider and, on first run, generates `backend.tf` and
> `provider.tf` from `root.hcl` and creates this unit's state blob.

```powershell
cd "$ENVDIR/resource-group"
terragrunt init
terragrunt plan
terragrunt apply

cd "../app-registration"
terragrunt init
terragrunt plan
terragrunt apply
```

**Before the first apply, read the plan output for the backend line.** It must reference
`tfstate-qa`. If it says `tfstate`, stop — Phase 1(b) did not take effect.

### Verify — Wave 0

```powershell
az group show --name $RG --query "{name:name, location:location, state:properties.provisioningState}" -o table
az ad app list --display-name $APPREG --query "[].{name:displayName, appId:appId}" -o table

# The state blob landed in the right container
az storage blob list --container-name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login -o table
```

**Correct result:** resource group `Succeeded`; app registration listed; the new container now holds
`resource-group/terraform.tfstate` and `app-registration/terraform.tfstate`.

### Execute — Wave 1

Plan every unit first, then apply. `--terragrunt-include-dir` was renamed in newer
Terragrunt releases and this repo pins no version, so the loop below avoids the flag
entirely:

```powershell
$wave1 = @("networking","observability","container-registry","cosmosdb","postgresql",
           "redis","servicebus","eventgrid","communication-services","governance")

# Pass 1 — plan everything, change nothing
foreach ($u in $wave1) {
  Write-Host "=== plan: $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"; terragrunt init; terragrunt plan
}
cd $ENVDIR
```

Then apply unit by unit. Applying individually on a first build means a failure names its own unit:

```powershell
foreach ($u in @("networking","observability","container-registry","cosmosdb","postgresql",
                 "redis","servicebus","eventgrid","communication-services","governance")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

> PostgreSQL and Cosmos DB are the slow ones — allow 10–15 minutes each.

### Verify — Wave 1

```powershell
# Everything created in the resource group
az resource list --resource-group $RG --query "[].{name:name, type:type, location:location}" -o table

# Individually
az network vnet show -g $RG -n $VNET --query "{name:name, subnets:length(subnets)}" -o table
az acr show -n $ACR --query "{name:name, sku:sku.name, login:loginServer}" -o table
az cosmosdb show -g $RG -n $COSMOS --query "{name:name, kind:kind, state:provisioningState}" -o table
az postgres flexible-server show -g $RG -n $PG --query "{name:name, state:state, version:version}" -o table
az servicebus namespace show -g $RG -n $SB --query "{name:name, sku:sku.name, state:status}" -o table
az eventgrid topic show -g $RG -n $EVGT --query "{name:name, state:provisioningState}" -o table
# Azure Managed Redis (azurerm_managed_redis) is NOT Azure Cache for Redis —
# `az redis show` targets the wrong provider. Look it up generically by name.
az resource list -g $RG --name $REDIS --query "[].{name:name, type:type, location:location}" -o table
az monitor log-analytics workspace show -g $RG -n $LOG --query "{name:name, sku:sku.name}" -o table
az monitor app-insights component show -g $RG -a $APPI --query "{name:name, appId:appId}" -o table
```

**Correct result:** the VNet reports **3** subnets (`aks`, `private-endpoints`, `gateway`); every
resource reports a succeeded/active state.

**Immediately stop PostgreSQL if the next wave is not starting now:**

```powershell
az postgres flexible-server stop -g $RG -n $PG
az postgres flexible-server show -g $RG -n $PG --query "state" -o tsv
```

---

## Phase 3 — Waves 2 and 3 (cluster, vault, identity)

### Understand

Wave 2 creates the cluster and the vault. Wave 3 creates the federated identities that let pods
reach the vault without secrets — and it can only run after the cluster exists, because a federated
credential is issued against the **cluster's OIDC issuer URL**, which does not exist until the
cluster does.

That ordering constraint is already encoded: `workload-identity` declares a dependency on `aks`, so
Terragrunt sequences it for you. This is worth understanding rather than trusting — it is the single
most interview-relevant piece of the build.

> This is federated identity: no secret is stored anywhere. The pod presents a token
> signed by the cluster, Entra trusts that specific issuer and subject, and hands back
> an Azure token. Change the ServiceAccount name and the trust no longer matches.

### Execute — Wave 2

```powershell
foreach ($u in @("aks","key-vault","github-oidc","function-app")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt init
  terragrunt plan
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

> **Check the Key Vault plan before approving.** Confirm `purge_protection_enabled = false`
> (Decision C). This is a one-way door — after apply it cannot be changed.

AKS takes 10–15 minutes.

### Verify — Wave 2

```powershell
# Cluster running, and its OIDC issuer exists (wave 3 depends on this)
az aks show -g $RG -n $AKS --query "{name:name, power:powerState.code, k8s:kubernetesVersion, oidc:oidcIssuerProfile.enabled, wi:securityProfile.workloadIdentity.enabled}" -o table
az aks show -g $RG -n $AKS --query "oidcIssuerProfile.issuerUrl" -o tsv

# Vault, and purge protection is off
az keyvault show -n $KV --query "{name:name, rbac:properties.enableRbacAuthorization, purge:properties.enablePurgeProtection, softDelete:properties.softDeleteRetentionInDays}" -o table

# The cluster can pull from the registry
az aks check-acr -g $RG -n $AKS --acr "$ACR.azurecr.io"

# Function app
az functionapp show -g $RG -n $FUNC --query "{name:name, state:state, https:httpsOnly}" -o table
```

**Correct result:** `powerState = Running`, OIDC issuer enabled with a URL, workload identity
enabled, vault `enablePurgeProtection` **null or false**, and `az aks check-acr` reporting success.

### Execute — Wave 3

```powershell
foreach ($u in @("workload-identity","role-assignments")) {
  Write-Host "=== $u ===" -ForegroundColor Cyan
  cd "$ENVDIR/$u"
  terragrunt init
  terragrunt plan
  terragrunt apply -auto-approve
  if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $u" -ForegroundColor Red; break }
}
cd $ENVDIR
```

### Verify — Wave 3

```powershell
# Six managed identities, one per service
az identity list -g $RG --query "[].{name:name, clientId:clientId}" -o table

# Federated credentials point at THIS cluster's issuer
$ISSUER = az aks show -g $RG -n $AKS --query "oidcIssuerProfile.issuerUrl" -o tsv
foreach ($mi in (az identity list -g $RG --query "[].name" -o tsv)) {
  Write-Host "--- $mi ---"
  az identity federated-credential list -g $RG --identity-name $mi `
    --query "[].{name:name, subject:subject, issuer:issuer}" -o table
}
Write-Host "Cluster issuer: $ISSUER"

# Role assignments landed on the vault, bus and topic
az role assignment list --scope $(az keyvault show -n $KV --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
az role assignment list --scope $(az servicebus namespace show -g $RG -n $SB --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
az role assignment list --scope $(az eventgrid topic show -g $RG -n $EVGT --query id -o tsv) `
  --query "[].{principal:principalName, role:roleDefinitionName}" -o table
```

**Correct result:** six identities (`gateway`, `products`, `cart`, `order`, `payments`, `discount`);
every federated credential's `issuer` matches the cluster issuer URL exactly; subjects read
`system:serviceaccount:antkart:ak-<service>`.

Expected role assignments:

| Identity | Key Vault | Service Bus | Event Grid |
|---|---|---|---|
| `ak-gateway` | Secrets User | — | — |
| `ak-products` | Secrets User | Data Sender + Receiver | — |
| `ak-cart` | Secrets User | Data Sender + Receiver | — |
| `ak-order` | Secrets User | Data Sender + Receiver | Data Sender |
| `ak-payments` | Secrets User | Data Sender + Receiver | Data Sender |
| `ak-discount` | Secrets User | — | — |

---

## Phase 4 — Post-provision: secrets and connectivity

### Understand

Terraform provisions the vault but does not seed application secrets — connection strings are read
from the resources *after* they exist. Nothing in the platform stores a secret in Git; each service
reads from Key Vault at startup via `DefaultAzureCredential`.

### Execute

```powershell
# --- PostgreSQL must be running to build/verify its connection string ---
az postgres flexible-server start -g $RG -n $PG

# Seed the vault. Values come from the resources you just created.
# See docs/guides/operations-command-reference.md for the full secret list and
# the exact connection-string formats used by dev.
```

> The authoritative secret names and formats live in
> [Operations Command Reference](operations-command-reference.md). Mirror the dev set exactly —
> the application code reads by name, so a renamed secret is a startup failure.

### Verify

```powershell
# Secrets present (names only — never print values)
az keyvault secret list --vault-name $KV --query "[].name" -o table

# Compare against dev to find anything missed
$devSecrets = az keyvault secret list --vault-name "kv-antkart-dev" --query "[].name" -o tsv
$newSecrets = az keyvault secret list --vault-name $KV --query "[].name" -o tsv
Compare-Object $devSecrets $newSecrets
```

**Correct result:** `Compare-Object` returns nothing — the two vaults hold the same secret names.

### Verify — cluster connectivity

```powershell
az aks get-credentials -g $RG -n $AKS --overwrite-existing
kubectl get nodes -o wide
kubectl get namespaces
```

**Correct result:** two nodes `Ready`.

---

## Phase 5 — GitOps and CD (session two)

> This phase is design work, not configuration. It is deliberately a separate session.

### 5.1 Cluster prerequisites

> **Why a pinned version and not `stable`.** The repo's other install docs use
> `argo-cd/stable`, which resolves to whatever is current at install time. The dev
> cluster runs v3.4.5 because that is what `stable` meant on the day it was installed.
> Using `stable` here would give this environment a different Argo CD than dev, so the
> version is pinned to match. Confirm with:
> `kubectl -n argocd get deploy argocd-server -o jsonpath='{.spec.template.spec.containers[0].image}'`

```powershell
kubectl create namespace antkart
kubectl create namespace argocd

# Argo CD — match the dev version
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/v3.4.5/manifests/install.yaml
kubectl -n argocd rollout status deploy/argocd-server --timeout=300s
```

### 5.2 Service accounts for workload identity

Each service needs a Kubernetes ServiceAccount annotated with its managed identity's client ID.
The federated credential subject created in Phase 3 is
`system:serviceaccount:antkart:ak-<service>` — the ServiceAccount name must match exactly, or token
exchange fails with a subject-mismatch error.

```powershell
# Retrieve the client IDs to annotate with
az identity list -g $RG --query "[].{name:name, clientId:clientId}" -o table
```

### 5.3 Helm values for the new environment

`deploy/helm/values/` holds one file per service, currently dev-targeted. The new environment needs
its own values — resource names, Key Vault URI, App Insights connection string, and the managed
identity client IDs.

**Open design decision — the promotion model.** There is no QA values set and no promotion
convention yet. Choose deliberately and record it as an ADR:

| Option | Shape | Trade-off |
|---|---|---|
| Values per environment | `values/qa/products.yaml` | Simple, explicit; duplicates common keys |
| Base + overlay | `values/base/` + `values/qa/` | DRY; more indirection |
| Separate GitOps repo | Per-environment repo | Cleanest separation; ADR-024 already names this as the long-term answer |

### 5.4 Argo CD project and applications

`deploy/argocd/appproject-antkart.yaml` and either `applications/` **or**
`applicationset-antkart.yaml` — never both, they create the same Application names.

> **The recurring trap.** Argo CD watches the chart and values an Application *points at*. It does
> **not** watch the Application manifest or the AppProject. Editing those in Git changes nothing
> until you `kubectl apply` them. This has caused real incidents on this platform. It is also why
> ADR-024 recommends a separate GitOps repository.

```powershell
kubectl apply -f deploy/argocd/appproject-antkart.yaml
kubectl apply -f deploy/argocd/applications/
kubectl get applications -n argocd
```

### 5.5 CD workflows

The `github-oidc` unit now issues an `environment:qa` claim. The CD workflows must declare
`environment: qa` for the token to match, and push image tags to the new environment's values path.

**Verify:**

```powershell
az ad app federated-credential list --id $(az ad app list --display-name $APPREG --query "[0].id" -o tsv) `
  --query "[].{name:name, subject:subject}" -o table
```

---

## Phase 6 — End-to-end verification

Run the Postman collection against the new environment's endpoint, then confirm telemetry.

```powershell
$WS = az monitor log-analytics workspace show -g $RG -n $LOG --query "customerId" -o tsv

# Traces spanning multiple services
az monitor log-analytics query --workspace $WS --analytics-query "union AppRequests, AppDependencies | where TimeGenerated > ago(60m) | summarize roles=make_set(AppRoleName), spans=count() by OperationId | where array_length(roles) > 1 | order by spans desc | take 5" -o table

# Structured logs carrying TraceId
az monitor log-analytics query --workspace $WS --analytics-query "ContainerLog | where TimeGenerated > ago(60m) | where LogEntry has 'TraceId' | project TimeGenerated, LogEntry | take 3" -o table
```

> **Schema note.** `AppRequests`, `AppDependencies` and `TimeGenerated` are the **workspace** schema
> and require `az monitor log-analytics query`. The classic `az monitor app-insights query` uses
> `requests`, `dependencies` and `timestamp` — passing workspace table names to it returns
> `BadArgumentError: The request had some invalid properties`, which looks like a KQL problem and is
> not one.

---

## Phase 7 — Teardown and cost control

### Daily stop

```powershell
az aks stop --name $AKS --resource-group $RG
az postgres flexible-server stop -g $RG -n $PG

# Always verify — never assume
az aks show --name $AKS --resource-group $RG --query "powerState.code" -o tsv
az postgres flexible-server list --query "[].{name:name, state:state}" -o table
```

Both must read `Stopped`.

### Full teardown

Reverse wave order — dependents before dependencies:

```powershell
foreach ($u in @("role-assignments","workload-identity","function-app","github-oidc","key-vault","aks",
                 "governance","communication-services","eventgrid","servicebus","redis","postgresql",
                 "cosmosdb","container-registry","observability","networking",
                 "app-registration","resource-group")) {
  Write-Host "=== destroying $u ===" -ForegroundColor Yellow
  cd "$ENVDIR/$u"
  terragrunt destroy -auto-approve
}
cd $ENVDIR
```

Because purge protection is `false` (Decision C), the vault can be purged immediately so the
environment can be rebuilt under the same names:

```powershell
az keyvault purge --name $KV --location $LOCATION
az keyvault list-deleted --query "[?name=='$KV'].name" -o tsv    # must return nothing
```

Delete the state container only if you intend to abandon the environment permanently:

```powershell
az storage container delete --name $STATE_CONTAINER --account-name $STATE_SA --auth-mode login
```

---

## Appendix A — Resource inventory

| Unit | Azure resource | Name pattern | Wave |
|---|---|---|---|
| `resource-group` | Resource Group | `rg-antkart-<env>-eastus` | 0 |
| `app-registration` | Entra App Registration | `antkart-api-<env>` | 0 |
| `networking` | Virtual Network + 3 subnets | `vnet-antkart-<env>-eastus` | 1 |
| `observability` | Log Analytics + App Insights | `log-antkart-<env>`, `appi-antkart-<env>` | 1 |
| `container-registry` | Container Registry | `acrantkart<env>` | 1 |
| `cosmosdb` | Cosmos DB (Mongo API) | `cosmos-antkart-<env>` | 1 |
| `postgresql` | PostgreSQL Flexible Server | `psql-antkart-<env>-eus2` | 1 |
| `redis` | Azure Managed Redis | `redis-antkart-<env>` | 1 |
| `servicebus` | Service Bus Namespace | `sb-antkart-<env>` | 1 |
| `eventgrid` | Event Grid Topic | `evgt-antkart-<env>` | 1 |
| `communication-services` | Communication Services | `antkart-<env>` prefix | 1 |
| `governance` | Budget + policy | `budget-antkart-<env>` | 1 |
| `aks` | AKS Cluster | `aks-antkart-<env>` | 2 |
| `key-vault` | Key Vault | `kv-antkart-<env>` | 2 |
| `github-oidc` | Federated credentials for CI/CD | — | 2 |
| `function-app` | Function App + storage | `func-antkart-notifications-<env>` | 2 |
| `workload-identity` | 6 managed identities + federated creds | `ak-<service>` | 3 |
| `role-assignments` | RBAC grants | — | 3 |

## Appendix B — Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| Plan proposes destroying live dev resources | State container not changed | Stop. Check `root.hcl` line 31 |
| `BadArgumentError` on a telemetry query | Workspace schema sent to the classic API | Use `az monitor log-analytics query` |
| Key Vault create fails, name unavailable | Soft-deleted vault holds the name | `az keyvault list-deleted`; purge if protection was off |
| Federated credential token exchange fails | ServiceAccount name ≠ credential subject | Compare `system:serviceaccount:antkart:ak-<svc>` to the SA |
| Argo shows no change after a Git edit | Edited an Application/AppProject manifest | `kubectl apply` it — Argo does not watch itself |
| Argo will not re-sync after a failure | Argo does not auto-retry the same revision | Sync manually |
| `az` strips inner double quotes on Windows | PowerShell quoting | Single quotes inside a double-quoted KQL string |
| Postgres SKU unavailable in `eastus` | Regional offer restriction | Provision in `eastus2` |
| `AllocationFailed` on Redis apply | Region temporarily at capacity | Try a nearby region and re-apply; note the change |
| `unknown flag: --terragrunt-include-dir` | Flag renamed in newer Terragrunt | Plan per unit instead of run-all |

## Appendix C — What this runbook does not yet cover

Honest gaps, to be closed as they are built:

- Seeding Cosmos with product data for the new environment.
- Razorpay test credentials and the payment verification path.
- cert-manager, ingress and DNS for a per-environment hostname.
- The Notification Function's Event Grid subscription wiring.
- An infrastructure CI/CD workflow. This runbook is its specification — automate it only after
  running it by hand at least once.

---

**Related:** [Infrastructure Guide](infrastructure-guide.md) · [GitOps Guide](gitops-guide.md) ·
[Operations Command Reference](operations-command-reference.md) ·
[Known Issues](../KNOWN_ISSUES.md)
