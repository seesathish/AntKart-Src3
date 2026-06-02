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

> _The exact provider configuration, credential setup, role assignments, and backend bootstrap commands are captured in the numbered sections below as each step is completed._

---

## Infrastructure Components

Each component below is documented with the same four-part structure. Sections are filled in as the component is built, capturing the real configuration, the real commands, and the real verification output.

### 1. Terraform Identity & Access (Service Principal, RBAC)

#### Understand
_To be completed as this component is built._

#### Build
_To be completed as this component is built._

#### Execute
_To be completed as this component is built._

#### Verify
_To be completed as this component is built._

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
