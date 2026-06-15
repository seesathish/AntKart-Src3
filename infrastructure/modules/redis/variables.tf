# =============================================================================
# Azure Cache for Redis module — inputs
# =============================================================================
# This module defines HOW the Redis cache is built. The environment supplies
# WHAT values to use. No environment values are baked in here.
#
# The cache backs the ShoppingCart service (cart state with a TTL). It is a
# single managed node — see main.tf for the Basic C0 rationale.

variable "resource_group_name" {
  description = "Name of the resource group the cache is created in (supplied from the Resource Group module's output)."
  type        = string
}

variable "location" {
  description = "Azure region for the Redis cache (supplied by the environment)."
  type        = string
}

variable "name" {
  description = "Name of the Redis cache. NOTE: cache names are GLOBALLY UNIQUE — they become part of the <name>.redis.cache.windows.net hostname (lowercase letters, numbers, hyphens)."
  type        = string
}

variable "capacity" {
  description = "Cache size within the family. For the Basic/Standard C family, 0 = C0 (~250 MB), the smallest node."
  type        = number
  default     = 0
}

variable "family" {
  description = "SKU family. 'C' is the Basic/Standard (non-clustered) family; 'P' is Premium."
  type        = string
  default     = "C"
}

variable "sku_name" {
  description = "Pricing tier. 'Basic' is a single node with no SLA (dev/test). 'Standard' adds replication/SLA; 'Premium' adds VNet/clustering/persistence."
  type        = string
  default     = "Basic"
}

variable "tags" {
  description = "Tags applied to the cache, consistent with the other units. Supplied by the environment; defaults to none."
  type        = map(string)
  default     = {}
}
