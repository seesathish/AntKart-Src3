# =============================================================================
# Azure Cache for Redis module — cache
# =============================================================================
# Provisions a managed Redis cache for the ShoppingCart service, which stores
# cart state keyed per user with a TTL.
#
# WHY BASIC C0: Basic is the cheapest tier — a SINGLE node (~250 MB) with NO
# replication and NO SLA. That is exactly right for dev/test, where a brief cache
# outage just means carts are rebuilt, not lost business. Standard would add a
# replica + SLA, and Premium would add clustering, persistence, and VNet
# injection — none of which a dev workload needs.
#
# ACCESS MODEL: connections are KEY-BASED over TLS. enable_non_ssl_port = false
# leaves ONLY the TLS port (6380) open, so the plaintext port (6379) is never
# exposed; clients authenticate with an access key (see outputs.tf). The key is a
# secret retrieved and stored in Key Vault in a later step — never committed.
#
# M5 HARDENING (deferred): private networking. VNet injection / private endpoints
# for Redis require the PREMIUM tier, so locking the cache to a virtual network
# is part of the M5 security-hardening step, not this dev provisioning.

resource "azurerm_redis_cache" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location

  # Basic C0: capacity 0, family "C", sku "Basic" — single ~250 MB node.
  capacity = var.capacity
  family   = var.family
  sku_name = var.sku_name

  # TLS-only: keep the non-SSL port (6379) CLOSED so traffic can only use the
  # encrypted port (6380).
  non_ssl_port_enabled = false

  # Reject anything older than TLS 1.2.
  minimum_tls_version = "1.2"

  tags = var.tags
}
