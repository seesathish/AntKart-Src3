# =============================================================================
# App Registration module — outputs
# =============================================================================
# client_id is a REAL value consumed by the application's auth configuration in
# the data/identity migration phase (the audience/authority the API validates
# against). No secrets are output — this app has none.

output "client_id" {
  description = "The application (client) ID — used by the application's auth configuration."
  value       = azuread_application.this.client_id
}

output "object_id" {
  description = "The application object's id in the directory."
  value       = azuread_application.this.object_id
}

output "service_principal_object_id" {
  description = "The service principal's object id (the usable principal in this tenant)."
  value       = azuread_service_principal.this.object_id
}

output "app_role_values" {
  description = "The defined app role values (e.g. [\"admin\", \"user\"]) that appear in the token roles claim."
  value       = [for r in var.app_roles : r.value]
}
