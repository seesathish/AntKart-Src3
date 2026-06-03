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
- **Authorization via RBAC roles.** Authenticating proves *identity*; it does not grant *permission*. The service principal is assigned **role-based access control (RBAC)** roles, scoped to the subscription (and, where needed, to specific resources), that allow it to create and manage exactly the resources it needs — and no more (least privilege).
- **Remote state with locking.** Terraform records what it has created in **state**. State is stored **remotely** (not on a single machine) so the team shares one source of truth, and it is protected by **locking** so two runs cannot modify the same infrastructure concurrently and corrupt it.

> The Terraform service principal and its RBAC assignment are created in **Step 1** below — with the Azure CLI, because the automation identity must exist before Terraform can authenticate as it. The remote state backend and Terragrunt root are bootstrapped in **Step 2**.

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

**Why Contributor specifically.** The **Contributor** role can create, read, update, and delete resources — everything an IaC identity needs — but it **cannot grant access to others** (it cannot create role assignments). Granting access requires **Owner** or **User Access Administrator**. Withholding that from the Terraform identity is a deliberate least-privilege choice: the automation that *builds* resources should not also be able to *hand out permissions*.

**How Terraform consumes this.** The `azurerm` provider authenticates as the service principal using four environment variables — `ARM_CLIENT_ID` (the SP's application id), `ARM_CLIENT_SECRET` (its secret), `ARM_TENANT_ID` (the directory), and `ARM_SUBSCRIPTION_ID` (where resources are created). When these are present, Terraform runs as the service principal with its Contributor permissions, with no `az login` and no interactive sign-in.

#### Build

This step has **no Terraform script**. There is a bootstrapping order: you cannot use Terraform to create the very identity Terraform signs in with, so the service principal and its role assignment are created once, up front, with the **Azure CLI**.

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

# 2. Set the four ARM_* environment variables for this session so Terraform
#    authenticates as the service principal. Use the values printed in step 1.
$env:ARM_CLIENT_ID       = "<appId>"
$env:ARM_CLIENT_SECRET   = "<password>"
$env:ARM_TENANT_ID       = "<tenant>"
$env:ARM_SUBSCRIPTION_ID = "<subscription-id>"
```

What each does:
- **Step 0** — sets the active subscription so the service principal and its role assignment land in the right place.
- **Step 1** — creates the `antkart-terraform-sp` service principal and assigns it **Contributor** at subscription scope; prints the credentials once.
- **Step 2** — exports the four `ARM_*` variables so the `azurerm` provider authenticates as the service principal for the rest of the session.

`az ad sp create-for-rbac` prints the password only once. If it is lost, reset it (see the security note) rather than recreating the service principal.

#### Verify

```powershell
# Confirm the role assignment: expect Contributor at the subscription scope.
az role assignment list --assignee "<appId>" -o table
```

A **correct result** shows one row with **Principal** = the SP's `appId`, **Role** = `Contributor`, and **Scope** = `/subscriptions/<subscription-id>`.

In the portal:
- **Microsoft Entra ID → App registrations** → search `antkart-terraform-sp` to see the identity.
- **Subscription → Access control (IAM) → Role assignments** → filter by the SP name → it appears with the **Contributor** role at subscription scope.

Optional end-to-end check once the variables are set: a later Terraform `plan` that authenticates without prompting confirms the credentials and permissions are wired correctly.

> **Security note.** The client secret (`ARM_CLIENT_SECRET`) is a credential equivalent to a password. Store it only in environment variables now, and in a secret store or pipeline secret later — **never** in the repository, a `.tf` file, or a committed variables file. Secrets can and should be rotated: `az ad sp credential reset --id <appId>` issues a new secret and invalidates the old one, without recreating the service principal or its role assignment.

### 2. Remote State Backend & Terragrunt Root

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 3. Resource Group

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 4. Networking (VNet, Subnets, NSGs)

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

### 5. Container Registry

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

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
