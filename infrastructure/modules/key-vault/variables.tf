# =============================================================================
# Key Vault module — inputs
# =============================================================================
# This module defines HOW the Key Vault is built. The environment supplies WHAT
# values to use. No environment-specific values are baked in here.

variable "resource_group_name" {
  description = "Name of the resource group the vault is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the vault (supplied from the Resource Group module's output)."
  type        = string
}

variable "key_vault_name" {
  description = "Name of the Key Vault. NOTE: vault names are GLOBALLY UNIQUE, 3-24 characters, alphanumeric and hyphens (must start with a letter). A recently deleted vault keeps its name reserved during soft-delete retention."
  type        = string
}

variable "tenant_id" {
  description = "Microsoft Entra (Azure AD) tenant id the vault is bound to. Optional: if left null, the module uses the tenant of the identity Terraform is running as (via data.azurerm_client_config), so the environment does not have to hardcode it."
  type        = string
  default     = null
}

variable "sku" {
  description = "Key Vault SKU: standard or premium (premium backs keys with HSMs)."
  type        = string
  default     = "standard"
}

variable "soft_delete_retention_days" {
  description = "Days a deleted vault (and its secrets) remain recoverable before permanent purge. 7 (the minimum) for a disposable dev vault; longer for production."
  type        = number
  default     = 7
}

variable "purge_protection_enabled" {
  description = "When true, a soft-deleted vault CANNOT be purged early — it must wait out the full retention period. Production sets this true to prevent irreversible loss. Dev leaves it false so a disposable vault can be purged and recreated cleanly during teardown."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to the vault, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
