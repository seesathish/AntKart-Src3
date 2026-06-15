# =============================================================================
# Azure Cache for Redis module — outputs
# =============================================================================
# These feed the Key Vault + service-wiring step. The access key and the
# connection string are secrets, marked sensitive so Terraform never prints
# them. They are NOT stored in the repo — a later step copies the connection
# string into Key Vault, from which the ShoppingCart service reads at runtime.

output "hostname" {
  description = "The Redis cache hostname (<name>.redis.cache.windows.net) clients connect to."
  value       = azurerm_redis_cache.this.hostname
}

output "ssl_port" {
  description = "The TLS port for client connections (6380). The non-TLS port is disabled."
  value       = azurerm_redis_cache.this.ssl_port
}

output "primary_access_key" {
  description = "The primary access key used to authenticate to the cache. Sensitive — consumed only to seed Key Vault in a later step; never commit or print it."
  value       = azurerm_redis_cache.this.primary_access_key
  sensitive   = true
}

output "connection_string" {
  description = "Ready-to-use StackExchange.Redis connection string (TLS, key auth). Sensitive — copied into Key Vault in a later step; never commit or print it."
  value       = "${azurerm_redis_cache.this.hostname}:${azurerm_redis_cache.this.ssl_port},password=${azurerm_redis_cache.this.primary_access_key},ssl=True,abortConnect=False"
  sensitive   = true
}
