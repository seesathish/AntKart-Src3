# Infrastructure

This directory holds the Infrastructure-as-Code for the platform's cloud environment, provisioned with **Terraform** and **Terragrunt**. For the concepts, the per-resource walkthroughs (Understand → Build → Execute → Verify), and the verification steps, see the [Infrastructure Guide](../docs/guides/infrastructure-guide.md).

## Layout

```
infrastructure/
├── modules/            Reusable Terraform modules (one per resource type)
└── environments/
    └── dev/            Terragrunt live configuration for the dev environment
```

- **`modules/`** — reusable, parameterised Terraform modules, one per resource type (resource group, networking, container registry, key vault, data stores, messaging, hosting, identity, and so on). A module describes *how* a resource is built: it takes inputs and exposes outputs, with no environment-specific values baked in.
- **`environments/dev/`** — the Terragrunt "live" configuration that wires the modules together for the `dev` environment, supplies their inputs, and manages the remote state backend. Additional environments are added as sibling folders under `environments/`.

## Status

The directory home is established here; the Terraform and Terragrunt configuration is added step by step as each component is built.

- The first identity step — the Terraform service principal and its RBAC assignment — has **no Terraform script**. It is created with the Azure CLI because the automation identity must exist before Terraform can authenticate (see **Step 1** of the [Infrastructure Guide](../docs/guides/infrastructure-guide.md)).
- **Step 2** adds the remote-state backend and the Terragrunt root configuration here, after which `modules/` and `environments/dev/` begin to fill in.
