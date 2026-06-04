# =============================================================================
# Terragrunt LIVE configuration — dev / Key Vault
# =============================================================================
# The deployable instance of the key-vault module for the dev environment. The
# module says HOW to build the vault; this file says WHAT values to use and
# wires in the resource group it depends on.

# include "root": inherit the shared remote state backend (Azure AD auth) and
# the shared azurerm provider from environments/dev/root.hcl.
include "root" {
  path = find_in_parent_folders("root.hcl")
}

# dependency: the vault lives in the resource group created by the
# resource-group unit. Terragrunt applies that unit FIRST and exposes its
# outputs here, so the real name and location are wired in — never hardcoded.
dependency "resource_group" {
  config_path = "../resource-group"

  # mock_outputs let init/plan/validate run before the dependency is applied
  # (e.g. on a clean checkout or in CI). A real apply uses the actual outputs.
  mock_outputs = {
    name     = "rg-antkart-dev-eastus"
    location = "eastus"
  }
  mock_outputs_allowed_terraform_commands = ["init", "plan", "validate"]
}

# terraform.source: the reusable key-vault module this unit runs.
terraform {
  source = "../../../modules/key-vault"
}

# inputs: this environment's values. The resource group name/location come from
# the dependency's outputs. The tenant id is intentionally NOT set here — the
# module resolves it from the current Azure context, so nothing is hardcoded.
inputs = {
  resource_group_name = dependency.resource_group.outputs.name
  location            = dependency.resource_group.outputs.location

  # NOTE: vault names are GLOBALLY UNIQUE, 3-24 chars, alphanumeric/hyphens. If
  # this name was used before and soft-deleted, it may still be reserved during
  # the retention window — recover/purge it or choose a new name.
  key_vault_name = "kv-antkart-dev"

  # Dev: leave purge protection off so a disposable vault can be purged and
  # recreated cleanly. Production would set this true.
  purge_protection_enabled = false

  tags = {
    environment = "dev"
    project     = "antkart"
    managed-by  = "terraform"
  }
}
