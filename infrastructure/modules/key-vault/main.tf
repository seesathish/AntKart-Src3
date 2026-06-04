# =============================================================================
# Key Vault module — resource
# =============================================================================
# A secure managed store for secrets, keys, and certificates. Built from inputs,
# so the same module produces a structurally identical vault in any environment.

# data.azurerm_client_config exposes the context Terraform is authenticated in,
# including the tenant id. Using it means the environment does not have to
# hardcode the tenant — the vault is bound to the same tenant Terraform runs in.
data "azurerm_client_config" "current" {}

locals {
  # Prefer an explicit tenant_id input if given; otherwise fall back to the
  # current authenticated tenant from the data source above.
  tenant_id = var.tenant_id != null ? var.tenant_id : data.azurerm_client_config.current.tenant_id
}

resource "azurerm_key_vault" "this" {
  name                = var.key_vault_name
  resource_group_name = var.resource_group_name
  location            = var.location
  tenant_id           = local.tenant_id
  sku_name            = var.sku

  # --- Access model: Azure RBAC, not legacy access policies ------------------
  # Key Vault supports two ways to authorize access:
  #   * Legacy "access policies" — a per-vault list mapping identities to
  #     allowed operations, managed separately from the rest of Azure.
  #   * Azure RBAC — the same role-based model used everywhere else, where roles
  #     like "Key Vault Secrets User" are assigned to identities at a scope.
  # We use RBAC so authorization is consistent with the whole platform and
  # managed in one place.
  rbac_authorization_enabled = true

  # --- Soft delete & purge protection ----------------------------------------
  # Soft delete is always on: a deleted vault enters a SOFT-DELETED state and its
  # NAME STAYS RESERVED for the retention period below, so the name cannot be
  # reused immediately (recover or purge it first).
  soft_delete_retention_days = var.soft_delete_retention_days

  # Purge protection, when enabled, prevents an early permanent purge — a
  # soft-deleted vault must wait out the full retention window. Production
  # enables it to make secret loss irreversible-proof; dev leaves it off so a
  # throwaway vault can be purged and recreated cleanly.
  purge_protection_enabled = var.purge_protection_enabled

  tags = var.tags

  # NOTE: application identities are NOT granted access here. The built-in
  # **Key Vault Secrets User** role (read secrets at runtime) is assigned to the
  # app/managed identities in the managed-identity step, not in this module.
}
