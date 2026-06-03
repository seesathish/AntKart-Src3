# =============================================================================
# Terragrunt ROOT configuration — dev environment
# =============================================================================
# This file is the single place that configures the Terraform *remote state
# backend* (and a shared provider) for every module in the dev environment.
# Each child module includes this root via:
#
#     include "root" {
#       path = find_in_parent_folders("root.hcl")
#     }
#
# and therefore inherits the same backend automatically — the configuration
# lives in exactly one place (DRY), so it cannot drift between modules.
#
# This root defines NO application resources by itself. It only wires up the
# backend and provider that the resource modules (Step 3 onward) build on.
# -----------------------------------------------------------------------------

# locals: the coordinates of the remote state backend. These describe WHERE
# Terraform state is stored, not what the platform deploys. They point at a
# dedicated state resource group / storage account, kept separate from the
# application resources so state is never removed by an app teardown.
locals {
  state_resource_group = "rg-antkart-tfstate"

  # NOTE: storage account names are GLOBALLY UNIQUE and must be 3-24 lowercase
  # alphanumeric characters. If this name is already taken, choose another and
  # update the bootstrap commands in the Infrastructure Guide to match.
  state_storage_account = "stantkarttfstate"

  state_container = "tfstate"
}

# remote_state: tells Terragrunt where to keep Terraform state and how to lock
# it, so the individual modules don't have to configure a backend themselves.
remote_state {
  # The azurerm backend stores state as a blob in Azure Storage. State locking
  # is built in: while an apply runs, the backend takes a *blob lease* on the
  # state file, so a second concurrent apply cannot acquire the lock and is
  # refused — operations are serialized and the state cannot be corrupted.
  backend = "azurerm"

  # generate: Terragrunt writes this backend definition into a `backend.tf`
  # file inside each child module at init time. That is how every module
  # inherits the same backend without copy-paste. `overwrite_terragrunt` keeps
  # the generated file in sync with this root on every run.
  generate = {
    path      = "backend.tf"
    if_exists = "overwrite_terragrunt"
  }

  config = {
    resource_group_name  = local.state_resource_group
    storage_account_name = local.state_storage_account
    container_name       = local.state_container

    # key: the path of the state blob WITHIN the container. It is derived from
    # each module's path relative to this root, so every module gets its own
    # isolated state file (e.g. "resource-group/terraform.tfstate") and two
    # modules can never write to the same state.
    key = "${path_relative_to_include()}/terraform.tfstate"

    # use_azuread_auth: authenticate to the storage backend with the caller's
    # Microsoft Entra (Azure AD) identity — the service principal from Step 1,
    # or an interactive `az login`. No storage account key is used or stored,
    # so there is no secret in this file or anywhere in the repository.
    # (State is also encrypted at rest by the storage account by default.)
    use_azuread_auth = true
  }
}

# generate "provider": Terragrunt writes a shared azurerm provider into every
# module at init time, so the provider is configured in exactly one place. It
# reads the ARM_* environment variables (set in Step 1) to authenticate as the
# Terraform service principal; no credentials appear here.
generate "provider" {
  path      = "provider.tf"
  if_exists = "overwrite_terragrunt"
  contents  = <<-EOF
    provider "azurerm" {
      # `features {}` is required by the azurerm provider; default behaviours
      # can be tuned per-resource later.
      features {}

      # Use the Microsoft Entra identity (from the ARM_* env vars) for storage
      # data-plane calls rather than a storage account key.
      storage_use_azuread = true
    }
  EOF
}

# inputs: values passed down to every child module. Environment-wide settings
# (such as the environment name) live here so each module doesn't repeat them.
# Resource-specific inputs are added by the modules from Step 3 onward.
inputs = {
  environment = "dev"
}
