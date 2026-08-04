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
$REPO_ROOT = (Get-Location).Path
$ENVDIR    = Join-Path $REPO_ROOT "infrastructure\environments\$ENV"

Write-Host "Environment=$ENV  RG=$RG  StateContainer=$STATE_CONTAINER"
```

> Run section 0.1 from the repository root. `$ENVDIR` is absolute so that `cd "$ENVDIR\<unit>"`
> works from any directory — a relative path resolves against the current location and
> fails once you are already inside a unit folder.

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
- **`⚠️ UNVERIFIED`** marks a step written from the repository but not yet executed against a live cluster. Treat it as a starting point, verify the result, and remove the marker once it is proven. Phases 0-3 carry no markers — they have been run end to end. Phase 4's secret seeding is marked pending its first full run; the rest of Phase 4 has been exercised.

### 0.4 The five ideas behind every command in this runbook

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

### 0.5 The one-way doors

Three things in this runbook cannot be undone cheaply. They are called out where they occur.

| Door | Why it is one-way | Phase |
|---|---|---|
| Key Vault purge protection | Azure will not let it be disabled once enabled; a deleted vault's name is locked for the retention window | 1.3 |
| Terraform state container name | Changing it after apply orphans every state blob | 0 |
| Globally unique resource names | Taken names cannot be reused while soft-deleted | 1.2 |

### 0.6 Where everything lives

Two folders do different jobs, and the difference is the core idea.

**`infrastructure/modules/`** holds 18 reusable blueprints — the `.tf` files that describe
*how* to build each resource type. They are shared by every environment and are never
copied. Nothing in this runbook edits them.

**`infrastructure/environments/`** holds one folder per environment. Each unit inside is a
single `terragrunt.hcl` that points at a module and supplies *this* environment's values.

Compare one resource:

```
infrastructure/modules/redis/             <- the blueprint (shared)
├── main.tf                               declares azurerm_managed_redis
├── variables.tf                          what it accepts (name, location, sku...)
└── outputs.tf                            what it hands to other units

infrastructure/environments/dev/redis/    <- the instance (per environment)
└── terragrunt.hcl                        "use ../../../modules/redis, name it
                                          redis-antkart-dev, put it in eastus2"
```

That is the whole pattern, repeated 18 times.

**Today:**

```
infrastructure/
├── modules/                              18 blueprints — shared
└── environments/
    └── dev/                              the only environment
        ├── root.hcl                      backend + provider settings for THIS environment
        ├── aks/terragrunt.hcl
        ├── redis/terragrunt.hcl
        └── ... 16 more units
```

**After this runbook:**

```
infrastructure/
├── modules/                              UNCHANGED — still 18, still shared
└── environments/
    ├── dev/
    │   ├── root.hcl                      state_container = "tfstate"
    │   └── ... 18 units
    └── qa/                               NEW — a sibling of dev, not a child
        ├── root.hcl                      state_container = "tfstate-qa" <-- the isolation lever
        └── ... 18 units, same names, qa values
```

Note `root.hcl` sits **inside** each environment folder, not above them. That is why each
environment can carry its own backend settings — and why the copy must be edited before
it is ever run.

**Why the state container is the whole safety story.** Terragrunt names each state blob
after the unit's path relative to `root.hcl`. Since `qa/redis` and `dev/redis` both resolve
to `redis`, their blob paths are *identical*. Different containers are what keeps them
apart:

```
Storage account: stantkarttfstate
├── tfstate/                              dev's 18 blobs — NEVER TOUCHED
│   ├── aks/terraform.tfstate
│   ├── redis/terraform.tfstate
│   └── ... 16 more
└── tfstate-qa/                           NEW container, identical internal layout
    ├── aks/terraform.tfstate
    ├── redis/terraform.tfstate
    └── ... 16 more
```

Same blob names, different containers, zero collision. Leave the container as `tfstate`
and QA writes straight over dev's records.

**Outside `infrastructure/`.** Two more places gain files, both in session two (Phase 5):

```
deploy/
├── helm/
│   ├── antkart-service/                  one generic chart — shared by all services and environments
│   └── values/                           6 dev value files today; QA values land here
│                                         (layout decided in Phase 5.3)
└── argocd/
    ├── appproject-antkart.yaml           the guard rails — what Argo may deploy and where
    └── applications/                     6 Application manifests, currently pointing at dev values

.github/workflows/                        12 workflows (6 CI + 6 CD)
                                          CD workflows gain QA targeting (Phase 5.5)
```

**What this runbook does NOT change:** `infrastructure/modules/`, the six service
codebases, the Helm chart itself, and every file under `environments/dev/`.

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

# (c2) The Terraform service principal needs an Entra DIRECTORY role to create
#      app registrations. Azure RBAC does not grant this.
az rest --method GET `
  --uri "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?`$filter=principalId eq '<terraform-sp-objectId>'" `
  --query "value[].roleDefinitionId" -o tsv

# (d) The four ARM_* variables are set for this shell
Get-ChildItem Env: | Where-Object { $_.Name -like "ARM_*" } | Select-Object Name
```

> **Two permission planes, not one.** Azure RBAC governs subscriptions and resources;
> Entra ID governs the directory — users, groups, app registrations. Contributor grants
> nothing in the directory. A service principal can create a resource group and still
> fail creating an app registration with:
>
> `Authorization_RequestDenied: Insufficient privileges to complete the operation`
>
> Fix: assign **Cloud Application Administrator** to the service principal in the portal
> under Entra ID → Roles and administrators. Allow ~2 minutes for propagation. The role
> assignment picker shows only users until you search by the SP's display name or appId.
>
> This is invisible in the first environment if its app registration was created
> interactively by an administrator rather than by the service principal.

> **(c3) Key Vault data plane.** With `enable_rbac_authorization = true`, Key Vault splits
> into two planes. Contributor covers the control plane — creating the vault, setting its
> properties. It grants nothing on the data plane, where secrets are read and written. A
> service principal can therefore create a vault and immediately fail writing a secret into
> it:
>
> ```
> Status=403 Code="Forbidden" ... Action: 'Microsoft.KeyVault/vaults/secrets/getSecret/action'
> Assignment: (not found)  InnerError={"code":"ForbiddenByRbac"}
> ```
>
> `Assignment: (not found)` means no data-plane role exists at all.
>
> Fix — grant **Key Vault Secrets Officer** (not Secrets User; Terraform must write):
>
> ```powershell
> az role assignment create `
>   --assignee-object-id "<terraform-sp-objectId>" `
>   --assignee-principal-type ServicePrincipal `
>   --role "Key Vault Secrets Officer" `
>   --scope $(az keyvault show -n $KV --query id -o tsv)
> ```
>
> Allow ~2 minutes for propagation, then re-run the unit. The vault is already in state, so
> only the secret is created.
>
> **The operator needs this as well.** The service principal grant covers Terraform. The
> human running this runbook is a separate principal and gets its own 403 when listing or
> writing secrets in Phase 4:
>
> ```
> Caller: appid=04b07795-8ddb-461a-bbee-02f9e1bf7b46 ... Assignment: (not found)
> ```
>
> That `appid` is the Azure CLI itself; the `oid` is the signed-in user. Grant yourself
> Secrets Officer on the new vault:
>
> ```powershell
> $myOid = az ad signed-in-user show --query id -o tsv
> az role assignment create `
>   --assignee-object-id $myOid `
>   --assignee-principal-type User `
>   --role "Key Vault Secrets Officer" `
>   --scope $(az keyvault show -n $KV --query id -o tsv)
> ```
>
> Three principals need data-plane access on a new vault, and each is a separate grant:
> the Terraform service principal (writes secrets during apply), the operator (seeds and
> verifies secrets in Phase 4), and the workload identities (read secrets at runtime —
> these are the only ones already in code, via the role-assignments unit).

`(c)` must show **Contributor** and **Role Based Access Control Administrator** at subscription
scope. `(d)` must list `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, `ARM_SUBSCRIPTION_ID`,
`ARM_TENANT_ID`. If they are missing, re-set them — see
[Infrastructure Guide § Terraform Identity & Access](infrastructure-guide.md).

### 1.1.1 Why these permission gaps are invisible in the first environment

> Three separate permission planes are involved, and they are easy to mistake for one:
>
> | Plane | Governs | Role needed |
> |---|---|---|
> | Azure RBAC | Subscriptions, resource groups, resources | Contributor + RBAC Administrator |
> | Entra ID directory | Users, groups, app registrations | Cloud Application Administrator |
> | Key Vault data plane | Reading and writing secrets | Key Vault Secrets Officer |
>
> A first environment often works without the second and third because an administrator
> performed those steps interactively — creating the app registration by hand, writing the
> first secrets under their own credentials. The service principal never needed the
> permission, so the gap was never visible.
>
> Building a second environment is what surfaces it. If the source environment's Key Vault
> shows a human account holding Secrets Officer and no service principal, that is the
> signature of exactly this.

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
robocopy "infrastructure\environments\dev" "infrastructure\environments\$ENV" /E `
  /XD ".terragrunt-cache" ".terraform" `
  /XF "*.tfplan" "backend.tf" "provider.tf" "versions.tf" `
  /NFL /NDL /NJH /NJS /NP

Write-Host "robocopy exit code: $LASTEXITCODE"
```

> **Expect a non-zero exit code.** Robocopy uses exit codes as a bitmask: 1 means files
> were copied successfully. Anything under 8 is success; 8 or above is a real failure.
>
> **Why robocopy and not `Copy-Item` + delete.** `.terragrunt-cache` holds downloaded
> provider binaries, and Windows locks them while any process has them open. Copying
> everything then deleting fails partway and leaves a half-cleaned tree. Excluding at
> copy time avoids the problem instead of cleaning up after it.

> **Keep `.terraform.lock.hcl`.** These pin exact provider versions and checksums, so
> the new environment resolves the same providers as dev. Do not delete and regenerate
> them — `init` would fetch whatever is current, and the two environments would diverge
> silently. Same failure mode as installing Argo CD from a floating `stable` tag.

`backend.tf`, `provider.tf` and `versions.tf` are **generated** by Terragrunt from `root.hcl` at init
time. Excluding the copies guarantees they are regenerated against the new backend rather than
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

> **Verify before continuing — do not assume this edit was applied:**
>
> ```powershell
> Select-String -Path "$ENVDIR/root.hcl" -Pattern "state_container|environment\s*="
> ```
>
> Must show `tfstate-<env>` and `environment = "<env>"`. This was missed once during a
> real build and only caught because a later step re-read the file. An unedited
> `root.hcl` points the new environment at dev's state — see Phase 0.

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
| Bare `"dev"` in tag arrays | `app-registration` | `["antkart", "dev"]` -> `["antkart", "<env>"]` |

> A resource-name pattern search will not catch a standalone `"dev"` inside a tag,
> list, or map value. Search for the bare quoted string separately.

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

> **Check the budget start date before applying `governance`.** The unit hardcodes
> `start_date`, and Azure rejects a monthly budget starting before the current month:
>
> `400: Start date for monthly time grain should not be prior to current month.`
>
> Set it to the first day of the current month in
> `$ENVDIR/governance/terragrunt.hcl`. Note that the source environment carries the same
> hardcoded date, so it can no longer be rebuilt from its own code either — worth an ADR
> to derive the date dynamically with a `lifecycle { ignore_changes }` guard.

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

> When purge protection is off, `enablePurgeProtection` returns **null**, which prints as a
> blank column rather than `false`. A blank value is the correct result — Azure only ever
> stores `true` or null for this property.

> `check-acr` writes a temporary file locally and can fail with
> `Permission denied when trying to write to ...\Temp\...` — a local filesystem problem
> (antivirus, sync client, folder permissions), not an Azure one. Pass `-f <writable-path>`,
> or verify the underlying fact directly:
>
> ```powershell
> az role assignment list --scope $(az acr show -n $ACR --query id -o tsv) `
>   --query "[].{principal:principalName, role:roleDefinitionName}" -o table
> ```
>
> Expect **AcrPull** (the AKS kubelet identity) and **AcrPush** (the CI/CD identity).

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

> Role assignments can fail on first apply with a principal-not-found error when the managed
> identity was created seconds earlier and is not yet visible to the RBAC service. This is
> transient — wait a minute and re-run the unit.

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

**Correct result:** **seven** identities — the six services (`gateway`, `products`, `cart`, `order`,
`payments`, `discount`) plus a CI/CD identity `id-ak-cicd-<env>` (which holds **AcrPush** on the
registry); every federated credential's `issuer` matches the cluster issuer URL exactly; subjects read
`system:serviceaccount:antkart:ak-<service>`.

> **Record the subjects now — Phase 5 depends on them.** Each federated credential's subject reads
> `system:serviceaccount:<namespace>:ak-<service>`. The Kubernetes ServiceAccount created in Phase 5.2
> must match character for character. A mismatch produces a token-exchange failure that reads as an
> authentication problem and is actually a naming problem.
>
> Also record the cluster's OIDC issuer URL and each identity's client ID — Phase 5.2 needs the
> client IDs for ServiceAccount annotations.

The Key Vault scope shows **eight** role assignments: the six service identities with Key Vault
Secrets User, the Function App's identity with Secrets User, and the Terraform service principal with
Secrets Officer.

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

> **⚠️ UNVERIFIED — the seeding procedure has not been run end to end.** The inventory below is the
> set a real build's vault held, with each secret's consumer confirmed from the repository, but the
> seeding itself is unproven. House rule throughout: **compare and verify by NAME only — never print a
> value.**

**(1) Build the work list.** Diff the new vault against the source environment; everything reported as
only in the source is still to seed:

```powershell
$srcSecrets = az keyvault secret list --vault-name "kv-antkart-dev" --query "[].name" -o tsv
$newSecrets = az keyvault secret list --vault-name $KV --query "[].name" -o tsv
Compare-Object $srcSecrets $newSecrets
```

**(2) Start PostgreSQL first.** Its connection strings cannot be built or tested while the server is
stopped:

```powershell
az postgres flexible-server start -g $RG -n $PG
```

Stop it again after Phase 4 (see Phase 7 → Daily stop), or it bills silently.

**(3) Seed each secret**, grouped by where its value comes from. **Match the source environment's
connection-string FORMAT exactly** — the application binds by both name and format (.NET maps the `--`
in a secret name to the `:` configuration separator, e.g. `ConnectionStrings--Postgres` →
`ConnectionStrings:Postgres`), so a renamed *or reshaped* secret is a **startup failure**, not a config
warning. Take the authoritative formats from
[Operations Command Reference](operations-command-reference.md) rather than the tables below, which
record only the name and consumer. Set each with `az keyvault secret set` without echoing the value.

Read from the new environment's Azure resources:

| Secret | Consuming service | Confirmed in repo |
|---|---|---|
| `ConnectionStrings--DiscountDb` | AK.Discount | `AK.Discount/AK.Discount.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — `GetConnectionString("DiscountDb")` |
| `ConnectionStrings--PaymentsDb` | AK.Payments | `AK.Payments/AK.Payments.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — `GetConnectionString("PaymentsDb")` |
| `ConnectionStrings--Postgres` | AK.Order (AK.Notification falls back to it) | `AK.Order/AK.Order.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — `GetConnectionString("Postgres")` |
| `ConnectionStrings--Notifications` | AK.Notification | `AK.Notification/AK.Notification.Core/Extensions/ServiceCollectionExtensions.cs` — `GetConnectionString("Notifications")` then falls back to `"Postgres"` |
| `MongoDbSettings--ConnectionString` | AK.Products | `AK.Products/AK.Products.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — binds the `MongoDbSettings` section |
| `RedisSettings--ConnectionString` | AK.ShoppingCart | `AK.ShoppingCart/AK.ShoppingCart.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — binds `RedisSettings`; `RedisContext` connects with `.ConnectionString` |
| `cosmos-connection-string` | ⚠️ UNVERIFIED — confirm during build | No service reads this key in the repo — AK.Products reads `MongoDbSettings--ConnectionString` instead. Likely a duplicate (see Appendix C) |
| `servicebus-connection-string` | ⚠️ UNVERIFIED — confirm during build | No service reads a Service Bus connection string — `AK.BuildingBlocks/AK.BuildingBlocks/Messaging/MassTransitExtensions.cs` connects via `ServiceBus:FullyQualifiedNamespace` + workload identity. Likely legacy |

Supplied by the operator (not derivable from Azure) — the Razorpay sandbox credentials:

| Secret | Consuming service | Confirmed in repo |
|---|---|---|
| `Razorpay--KeyId` | AK.Payments | `AK.Payments/AK.Payments.Infrastructure/Extensions/ServiceCollectionExtensions.cs` — binds the `Razorpay` section → `RazorpaySettings` |
| `Razorpay--KeySecret` | AK.Payments | same |

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

> Takes a provisioned-but-empty cluster (end of Phase 4) to all six services running and reconciled
> by Argo CD. **The whole of Phases 5 and 6 is written from the repository, not yet executed against
> a freshly-built environment** — every section that runs commands is marked `⚠️ UNVERIFIED`. Each
> follows the house pattern: **Understand → Execute → Verify → If it fails.**

### 5.0 Decisions to make before starting

No commands — decide these first. **5.3 onward cannot proceed until the promotion model is chosen**:
it determines where the new environment's Helm values live and what path every Argo CD Application
points at. Today there is exactly ONE values set under `deploy/helm/values/` (dev-targeted), so any
choice below is a new convention worth an ADR.

| Decision | Options | Consequence |
|---|---|---|
| **Promotion model** | (a) values per environment (`deploy/helm/values/<env>/<svc>.yaml`); (b) base + overlay (`values/base/` + `values/<env>/`); (c) separate GitOps repo per environment | (a) simplest, explicit, duplicates common keys; (b) DRY, more indirection; (c) cleanest isolation — **ADR-024 already names a separate repo as the long-term answer** |
| **App delivery object** | `applicationset-antkart.yaml` (**RECOMMENDED** in `deploy/argocd/README.md`) vs the six `applications/ak-*.yaml` | Apply **ONE, never both** — they create the same Application names |
| **Ingress controller** | reuse dev's ingress-nginx approach vs install fresh per environment | A shared controller = one public IP + one DNS story; a fresh per-env controller isolates blast radius but needs its own IP + DNS record |
| **CD trigger** | CD workflows target this environment vs deploy manually first | The `github-oidc` unit issues an `environment:<env>` claim (5.8); a workflow must declare that GitHub Environment for its OIDC token to match. Manual-first (`kubectl apply` the Applications) de-risks the first build |

### 5.1 ⚠️ UNVERIFIED — Cluster prerequisites

**Understand.** Argo CD runs inside the cluster and reconciles Git (pull-based). Pin its version to
match dev — `stable` resolves to whatever is current at install time, which would give this
environment a different Argo CD than dev.

> **Why a pinned version and not `stable`.** The repo's other install docs use `argo-cd/stable`,
> which resolves to whatever is current at install time. The dev cluster runs v3.4.5 because that is
> what `stable` meant on the day it was installed. Using `stable` here would give this environment a
> different Argo CD than dev, so the version is pinned to match.

**Execute.**

```powershell
kubectl create namespace antkart
kubectl create namespace argocd

# Argo CD — match the dev version
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/v3.4.5/manifests/install.yaml
kubectl -n argocd rollout status deploy/argocd-server --timeout=300s
```

**Verify.** Namespaces exist, argocd pods `Running`, and the server image matches the pinned version:

```powershell
kubectl get namespace antkart argocd
kubectl -n argocd get pods
kubectl -n argocd get deploy argocd-server -o jsonpath='{.spec.template.spec.containers[0].image}'
```

The image tag must read `v3.4.5`.

**If it fails.** A pod stuck `Pending` usually means the two-node cluster is out of schedulable
capacity — `kubectl -n argocd describe pod <name>` shows the scheduling reason.

### 5.2 ⚠️ UNVERIFIED — Service accounts for workload identity

**Understand.** The chart templates one ServiceAccount per service in
`deploy/helm/antkart-service/templates/serviceaccount.yaml`. It is named
`{{ include "antkart-service.name" . }}` (i.e. `ak-<service>`, from each values file's `name`) in
`{{ .Values.namespace }}` (default `antkart`), and carries:

- the label `azure.workload.identity/use: "true"` — enables the workload-identity mutating webhook,
- the annotation `azure.workload.identity/client-id: {{ .Values.workloadIdentityClientId }}` — the
  managed identity's client id, `required` and supplied **from values, never hardcoded**.

So the ServiceAccount is created by Helm/Argo, not by hand. What you must get right is the
`workloadIdentityClientId` in each values file (5.3) and the SA-name ↔ federated-subject match.

**Execute.** List the environment's managed-identity client IDs to fill into values:

```powershell
az identity list -g $RG --query "[].{name:name, clientId:clientId}" -o table
```

**Verify.** The rendered SA name must equal the federated credential subject
`system:serviceaccount:<namespace>:<sa-name>` created in Phase 3 (`workload-identity` unit). Compare
both sides:

```powershell
# The SAs the chart will create (namespace/name):
kubectl get serviceaccount -n antkart -o "custom-columns=NS:.metadata.namespace,NAME:.metadata.name"

# The federated subjects the identities actually trust:
foreach ($id in (az identity list -g $RG --query "[].name" -o tsv)) {
  az identity federated-credential list --identity-name $id -g $RG --query "[].{cred:name, subject:subject}" -o table
}
```

Each SA `antkart/ak-<service>` must have a matching subject `system:serviceaccount:antkart:ak-<service>`.

**If it fails.** A mismatch surfaces at runtime, not deploy time: the pod's token exchange is
rejected with an `AADSTS70021`-style "no matching federated identity record for the presented
assertion subject" error in the pod logs. The match is exact and case-sensitive — align the values
`name`/namespace with the federated-credential subject.

### 5.3 ⚠️ UNVERIFIED — Helm values for the new environment

**Understand.** `deploy/helm/values/` holds one file per service (products, cart, discount, order,
payments, gateway); shared defaults live in `deploy/helm/antkart-service/values.yaml` and Helm
deep-merges each per-service `env` map on top of them. The **environment-specific** keys — every one
carrying a `dev` value today — read from the repo:

| Key | File(s) | dev value → change to |
|---|---|---|
| `image.registry` | `antkart-service/values.yaml` | `acrantkartdev.azurecr.io` → `acrantkart<env>.azurecr.io` |
| `env.KeyVault__Uri` | `antkart-service/values.yaml` (the one shared secret-store default) | `https://kv-antkart-dev.vault.azure.net/` → new vault URI |
| `workloadIdentityClientId` | every `values/<svc>.yaml` | the per-service managed-identity client id (from `az identity list`, 5.2) |
| `env.ServiceBus__FullyQualifiedNamespace` | products, cart, order, payments | `sb-antkart-dev.servicebus.windows.net` → `sb-antkart-<env>...` |
| `env.EventGrid__TopicEndpoint` | order, payments | `https://evgt-antkart-dev.eastus-1.eventgrid.azure.net/api/events` → the new topic endpoint |
| `env.Entra__Audience` | products, cart, order, payments, gateway | `api://antkart-api-dev` → `api://antkart-api-<env>` |
| `env.Entra__ClientId` | products, cart, order, payments, gateway | the API app-registration client id (per env) |
| `ingress.host` | Argo Helm parameter (5.5); default empty in `antkart-service/values.yaml`, `api.antkart.in` for the gateway | the new environment hostname |

Keys that are **NOT** environment-specific (leave them): `name`/`serviceName` (in-cluster identity),
`env.Entra__TenantId` (same tenant `4cacc56a-…`) and `env.Entra__Instance`, the in-cluster DNS
overrides (`DiscountGrpc__Address`, `ProductsApi__BaseUrl`), `RedisSettings__*`, `MongoDbSettings__*`,
`image.name` (cart→`shoppingcart`), and `image.tag` (owned by CD).

> **The App Insights connection string is NOT a Helm value.** An earlier draft listed it as
> environment-specific, but the repo sets it in no values file — it is vaulted as the
> `ApplicationInsights--ConnectionString` secret and read from Key Vault at runtime. It is handled in
> secret seeding (5.6). Likewise every connection string / API key (`ConnectionStrings:Postgres`,
> `RedisSettings:ConnectionString`, the Razorpay keys) is a Key Vault secret, not a value.

**Per-service checklist.** For each values file change: `workloadIdentityClientId`; any
`ServiceBus__FullyQualifiedNamespace`; `EventGrid__TopicEndpoint` (order/payments only);
`Entra__Audience` + `Entra__ClientId` (all but `discount` — it injects no `Entra__*`). Change the two
shared defaults (`image.registry`, `env.KeyVault__Uri`) once in `antkart-service/values.yaml`, or
override per-env per the promotion model chosen in 5.0.

**Verify.** `helm template` the chart with the new values renders without error:

```powershell
helm template ak-products deploy/helm/antkart-service -f <new-env values path>/products.yaml
```

(the path depends on the 5.0 promotion model.)

**If it fails.** `Error: … values.workloadIdentityClientId is required` (from
`serviceaccount.yaml`) means that key is missing from the values file.

### 5.4 ⚠️ UNVERIFIED — Container registry access

**Understand.** The kubelet pulls images from the environment's ACR (`image.registry` =
`acrantkart<env>.azurecr.io`). AKS authenticates by its kubelet identity holding **AcrPull** on the
registry. If the cluster and ACR were provisioned by the same environment's Terraform this is already
wired, but a new environment must confirm it.

**Execute / Verify.**

```powershell
az aks check-acr --name $AKS --resource-group $RG --acr "acrantkart$ENV.azurecr.io"
```

**If it fails.** Grant AcrPull to the cluster's kubelet identity:

```powershell
$ACR_ID  = az acr show -n "acrantkart$ENV" --query id -o tsv
$KUBELET = az aks show -g $RG -n $AKS --query "identityProfile.kubeletidentity.objectId" -o tsv
az role assignment create --assignee-object-id $KUBELET --assignee-principal-type ServicePrincipal --role AcrPull --scope $ACR_ID
```

Symptom if skipped: pods stay `ImagePullBackOff` with a 401/403 from the registry.

### 5.5 ⚠️ UNVERIFIED — Argo CD project and applications

**Understand.** Three manifests in `deploy/argocd/`: `appproject-antkart.yaml` (the least-privilege
AppProject — **apply FIRST**), then EITHER `applicationset-antkart.yaml` (**RECOMMENDED** — templates
all six from one `elements` list) OR the six `applications/ak-*.yaml` (the alternative). **Apply one,
never both** — they create the same Application names. Each Application (or ApplicationSet element)
points at the chart (`path: deploy/helm/antkart-service`) with `valueFiles: [ ../values/<svc>.yaml ]`;
the gateway additionally sets `ingress.enabled=true`, `ingress.host=api.antkart.in`,
`ingress.clusterIssuer=letsencrypt-prod` as Helm parameters.

> **The recurring trap.** Argo CD watches the chart and values an Application *points at*. It does
> **not** watch the Application manifest or the AppProject. Editing those in Git changes nothing
> until you `kubectl apply` them. This has caused real incidents on this platform. It is also why
> ADR-024 recommends a separate GitOps repository.

**Point it at the new environment.** Change the `valueFiles` path in each `applications/ak-*.yaml`
(or the ApplicationSet's `valueFile` per element) to the new environment's values path chosen in 5.0,
and the gateway's `ingress.host` parameter to the new hostname. If you keep both files, keep the
ApplicationSet element list and `applications/ak-gateway.yaml` in sync — but still apply only one.

**Execute.**

```powershell
kubectl apply -f deploy/argocd/appproject-antkart.yaml
# choose ONE of the next two — never both:
kubectl apply -f deploy/argocd/applicationset-antkart.yaml
# kubectl apply -f deploy/argocd/applications/
```

**Verify.**

```powershell
kubectl get applications -n argocd
```

All six show `Synced` / `Healthy`.

**If it fails.** Argo CD does **not** auto-retry a failed sync for the same Git revision — sync it
manually (`argocd app sync <name>` or the UI) after fixing the cause. An `OutOfSync`/`Degraded` app
on a first build is almost always missing secrets (5.6) → `CrashLoopBackOff`.

### 5.6 ⚠️ UNVERIFIED — Secret seeding

**Understand.** Pods cannot start without their Key Vault secrets — every service reads connection
strings / API keys from Key Vault at startup via `DefaultAzureCredential`. This is Phase 4's secret
step, called out here because ordering is load-bearing: **it must happen before Argo syncs**, or the
first sync produces `CrashLoopBackOff` as each pod fails to read its secrets. Seed the vault as in
[Phase 4 → Execute](#phase-4--post-provision-secrets-and-connectivity); the authoritative secret
names and formats live in [Operations Command Reference](operations-command-reference.md).

**Verify.** Compare secret **names** (never values) against the source environment:

```powershell
$devSecrets = az keyvault secret list --vault-name "kv-antkart-dev" --query "[].name" -o tsv
$newSecrets = az keyvault secret list --vault-name $KV --query "[].name" -o tsv
Compare-Object $devSecrets $newSecrets
```

`Compare-Object` returns nothing — the two vaults hold the same secret names.

**If it fails.** A pod in `CrashLoopBackOff` right after the first sync: `kubectl logs -n antkart
<pod>` shows a Key Vault / config-binding error naming the missing secret. Seed it, then let Argo
re-sync (or restart the deployment).

### 5.7 ⚠️ UNVERIFIED — Ingress and cert-manager

**Understand.** The gateway is the only externally-exposed service, over HTTPS with a Let's Encrypt
certificate. `deploy/cert-manager/` already contains the two `ClusterIssuer` manifests —
`cluster-issuer-staging.yaml` (`letsencrypt-staging`) and `cluster-issuer-prod.yaml`
(`letsencrypt-prod`), both HTTP-01 over the `nginx` ingress class. It does **not** contain
cert-manager itself or the ingress: cert-manager is installed separately and the ingress is rendered
by the chart when the gateway Application sets `ingress.enabled=true` (see
`deploy/cert-manager/README.md`, which points to `docs/guides/aks-guide.md#ingress-and-tls`).

**Ordering matters.** HTTP-01 validation needs the DNS hostname resolving to the ingress controller's
public IP **before** the certificate can issue. So DNS comes *after* the IP is known and *before* the
certificate is expected.

**Execute.** ⚠️ UNVERIFIED — determine the exact controller/cert-manager install (and pinned
versions) from `docs/guides/aks-guide.md#ingress-and-tls` during the build.

```powershell
# 1. Install cert-manager and the ingress-nginx controller (versions per aks-guide).

# 2. Apply the ClusterIssuers (edit the placeholder email in each file first).
kubectl apply -f deploy/cert-manager/cluster-issuer-staging.yaml
kubectl apply -f deploy/cert-manager/cluster-issuer-prod.yaml

# 3. The gateway Application already sets ingress.enabled=true + host — get the controller's IP.
kubectl get ingress -n antkart
kubectl get svc -A -o wide | Select-String "LoadBalancer"   # EXTERNAL-IP of the ingress controller

# 4. Create the DNS A record: <env hostname> -> that public IP (registrar / Azure DNS).

# 5. Watch the certificate issue.
kubectl get certificate -n antkart -w
```

> **Start on staging.** Point the gateway's `ingress.clusterIssuer` at `letsencrypt-staging` first
> (generous rate limits; untrusted root — verify with `curl -k`). Only switch to `letsencrypt-prod`
> once staging issues `Ready`. **Let's Encrypt production rate limit: 5 duplicate certificates per
> identical set of names per 168 hours (1 week)** (plus 50 certs/week per registered domain) — a
> retry loop on prod can lock the hostname out for a week.

**Verify.**

```powershell
kubectl get certificate -n antkart                 # READY = True
curl https://<env hostname>/health/live            # 200 over trusted TLS (use -k on staging)
```

**If it fails.** Describe the objects in dependency order — `Certificate`, then `CertificateRequest`,
then `Order`:

```powershell
kubectl describe certificate -n antkart <name>
kubectl describe certificaterequest -n antkart
kubectl describe order -n antkart
```

A stuck `Order` almost always means DNS is not yet resolving to the ingress IP (step 4) or the
HTTP-01 challenge path is not reachable.

### 5.8 ⚠️ UNVERIFIED — CD workflows targeting this environment

**Understand.** The `github-oidc` unit issues a federated credential whose subject is
`repo:seesathish/AntKart-Src3:environment:<env>` (its `subjects` list carries
`{ name = "env-<env>", claim = "environment:<env>" }`). For a CD workflow's OIDC token to match, the
workflow job must declare that GitHub Environment. So the workflows need, for this environment:

- a job-level `environment: <env>` declaration, so the token carries `environment:<env>`;
- the new environment's values path for the image-tag bump step (the CD commit that updates
  `image.tag`), matching the promotion model from 5.0.

**Verify.** Confirm the federated credential subject exists on the app registration:

```powershell
az ad app federated-credential list --id $(az ad app list --display-name $APPREG --query "[0].id" -o tsv) `
  --query "[].{name:name, subject:subject}" -o table
```

Expect a row whose subject ends `environment:<env>`.

**If it fails.** A CD run failing at the Azure login step with a no-matching-federated-credential
error (`AADSTS700213`-style) means the workflow's `environment:` (or branch/subject) does not match
any credential subject — align the workflow declaration with the `github-oidc` subject.

---

## Phase 6 — End-to-end verification

> An ordered sequence from cluster-green to Postman-green over HTTPS. **All ⚠️ UNVERIFIED** — written
> from the repo, to be proven on the first real build. Each step has its own Verify.

### 6.1 ⚠️ UNVERIFIED — Pods healthy

```powershell
kubectl get pods -n antkart -o wide
```

**Verify.** All six `ak-*` pods `Running` with `RESTARTS 0` (products runs `replicaCount: 2`, so
expect two of its pods). A restart count climbing is a boot problem — usually missing secrets (5.6).

### 6.2 ⚠️ UNVERIFIED — Workload identity working

A pod must read a Key Vault secret with no stored credential — the whole point of workload identity.

```powershell
kubectl logs -n antkart deploy/ak-products | Select-String -Pattern "KeyVault|DefaultAzureCredential|AADSTS|Started"
```

**Verify.** Startup logs show Key Vault loaded and Kestrel started, with no `DefaultAzureCredential`
/ `AADSTS` errors. (A service cannot pass its readiness probe without its secrets, so a `Running`,
`Ready` pod is already strong evidence.)

**If it fails.** An `AADSTS70021` subject mismatch (5.2) or a missing AcrPull/secret appears in the
logs. `kubectl describe pod -n antkart <pod>` shows `ImagePullBackOff` (5.4) vs `CrashLoopBackOff`
(5.6).

### 6.3 ⚠️ UNVERIFIED — Gateway reachable over HTTPS

```powershell
curl https://<env hostname>/health/live
curl https://<env hostname>/gateway/health/products
```

**Verify.** Both return `200` over trusted TLS. `/gateway/health/{products|cart|orders|payments}` are
the gateway's health passthrough routes (defined in `deploy/helm/values/gateway.yaml`); a 200 through
one proves the ingress → gateway → downstream path end to end.

### 6.4 ⚠️ UNVERIFIED — Postman environment for this environment

The collection `AntKart-Cloud-E2E-Saga-Positive.postman_collection.json` uses collection variables
`baseUrl` and `entraTenantId`, and expects two environment values `entraClientId` and
`razorpayKeySecret`. Its collection-level OAuth 2.0 (Authorization Code + PKCE) authorizes against
`https://login.microsoftonline.com/{{entraTenantId}}/oauth2/v2.0/authorize` with
`clientId = {{entraClientId}}` and `scope = api://antkart-api-dev/access_as_user openid profile offline_access`.

Change for the new environment:

| Variable | Where | Change |
|---|---|---|
| `baseUrl` | collection variable | → `https://<env hostname>` (the new gateway HTTPS URL) |
| `entraTenantId` | collection variable | same tenant `4cacc56a-…` (change only for a different tenant) |
| `entraClientId` | environment value | the **public-client** app-registration id for this environment |
| **scope** | collection Authorization tab | `api://antkart-api-dev/access_as_user` → `api://antkart-api-<env>/access_as_user` (the API's App ID URI — from the `app-registration` unit output) |
| `razorpayKeySecret` | environment value | from the new environment's Key Vault |

### 6.5 ⚠️ UNVERIFIED — Run the collection

Run with the **Collection Runner**, *Delay between requests* = 8000 ms (the saga is asynchronous and
steps 07/12 self-retry).

**Verify.** All 12 ordered requests pass and the order reaches `Paid` — the payment-success journey
end to end against the new environment through HTTPS.

### 6.6 ⚠️ UNVERIFIED — Telemetry

```powershell
$WS = az monitor log-analytics workspace show -g $RG -n $LOG --query "customerId" -o tsv

# Traces spanning multiple services
az monitor log-analytics query --workspace $WS --analytics-query "union AppRequests, AppDependencies | where TimeGenerated > ago(60m) | summarize roles=make_set(AppRoleName), spans=count() by OperationId | where array_length(roles) > 1 | order by spans desc | take 5" -o table

# Structured logs carrying TraceId
az monitor log-analytics query --workspace $WS --analytics-query "ContainerLog | where TimeGenerated > ago(60m) | where LogEntry has 'TraceId' | project TimeGenerated, LogEntry | take 3" -o table
```

**Verify.** A healthy result: the first query returns at least one `OperationId` whose `roles` set
spans multiple services (e.g. `ak-gateway` + `ak-products` + `ak-order`) — proof a request was traced
across roles; the second returns structured log lines carrying a `TraceId`, correlatable back to that
`OperationId`.

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

> **Azure force-starts a stopped flexible server after 7 days.** The stop command says so
> in its output. Set a reminder to re-stop it, or the environment resumes billing silently.

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
| `InsufficientCapacity` on Redis apply | Region temporarily at capacity | Capacity is often transient — delete the failed resource, confirm `terragrunt state list` is empty, and retry before changing region. A successful create takes ~7 minutes; a capacity rejection fails in under 30 seconds. |
| Failed create left a resource holding the name | Azure records the shell even on failure | `az resource delete`, then verify `terragrunt state list` is empty before retrying |
| `unknown flag: --terragrunt-include-dir` | Flag renamed in newer Terragrunt | Plan per unit instead of run-all |
| `--name expected one argument` | Shell variables not set in this session | Re-run section 0.1 |
| `-o was unexpected at this time` | Windows `az` wrapper strips quotes; `(` breaks cmd parsing | Avoid parentheses in `--query`; use `--query "[].name"` then `.Count` in PowerShell |
| `ForbiddenByRbac` writing a Key Vault secret | No data-plane role on an RBAC-enabled vault | Grant Key Vault Secrets Officer at the vault scope; wait ~2 min |
| `check-acr` permission denied on a Temp path | Local filesystem, not Azure | Use `-f <path>`, or list role assignments on the ACR scope instead |
| `ForbiddenByRbac` listing secrets as yourself | The operator has no data-plane role on the new vault | Grant yourself Key Vault Secrets Officer; note this is separate from the service principal's grant |

## Appendix C — What this runbook does not yet cover

Honest gaps, to be closed as they are built:

- Seeding Cosmos with product data for the new environment.
- Razorpay test credentials and the payment verification path.
- The Notification Function's Event Grid subscription wiring.
- An infrastructure CI/CD workflow. This runbook is its specification — automate it only after
  running it by hand at least once.

> **Two secret naming conventions coexist.** The source environment carries both `Section--Key` names
> (which .NET configuration binds automatically) and kebab-case names such as `cosmos-connection-string`
> and `servicebus-connection-string`. Some appear to duplicate each other — `cosmos-connection-string`
> and `MongoDbSettings--ConnectionString` may hold the same value. Before seeding a new environment,
> confirm which names the application actually reads; recreating unused secrets copies the confusion
> forward.

---

**Related:** [Infrastructure Guide](infrastructure-guide.md) · [GitOps Guide](gitops-guide.md) ·
[Operations Command Reference](operations-command-reference.md) ·
[Known Issues](../KNOWN_ISSUES.md)
