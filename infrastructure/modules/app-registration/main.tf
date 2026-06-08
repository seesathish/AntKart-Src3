# =============================================================================
# App Registration module — Entra ID application + service principal
# =============================================================================
# Registers the platform's API application in Microsoft Entra ID. Registering
# gives the app two things:
#   * an IDENTITY  — a client/application ID it authenticates as, and
#   * a CONTRACT   — the app roles it defines and the audience (identifier URI)
#                    that APIs validate incoming tokens against.

# The directory context Terraform is running in (used to set the app owner).
data "azuread_client_config" "current" {}

locals {
  # Derive the identifier URI from the display name if one wasn't supplied.
  identifier_uri = coalesce(var.identifier_uri, "api://${var.display_name}")
}

resource "azuread_application" "this" {
  display_name = var.display_name

  # identifier_uris is the AUDIENCE: tokens issued for this app carry this as
  # their `aud` claim, and the API validates it so a token minted for another
  # app can't be replayed against this one.
  identifier_uris = [local.identifier_uri]

  # The creating identity owns the app (so it can manage it afterwards).
  owners = [data.azuread_client_config.current.object_id]

  # App roles. Each role here surfaces in an issued token as a value in a FLAT
  # `roles` claim (e.g. "roles": ["admin"]). The application reads that claim to
  # authorize. (Note: some other identity providers nest role claims under a
  # different path — consumers must read the correct claim for the issuer.)
  dynamic "app_role" {
    for_each = var.app_roles
    content {
      id                   = app_role.value.id
      allowed_member_types = app_role.value.allowed_member_types
      description          = app_role.value.description
      display_name         = app_role.value.display_name
      value                = app_role.value.value
      enabled              = true
    }
  }

  # Expose the API with a delegated scope, so callers can request tokens for it.
  api {
    oauth2_permission_scope {
      id                         = "3d0a3f4c-5e6f-6a7b-0c1d-2e3f4a5b6c7d"
      type                       = "User"
      value                      = "access_as_user"
      admin_consent_display_name = "Access the API as the signed-in user"
      admin_consent_description  = "Allow the application to access the API on behalf of the signed-in user."
      enabled                    = true
    }
  }

  tags = var.tags
}

# A service principal makes the application a USABLE principal in this tenant —
# the local representation that can be assigned roles and hold tokens. The
# application is the global definition; the service principal is its instance
# in the directory.
resource "azuread_service_principal" "this" {
  client_id = azuread_application.this.client_id
}

# NOTE: no client SECRET is created here. This application is an API that
# VALIDATES tokens — it does not need a credential to request them. Callers that
# need to request tokens manage their own credentials (or use managed
# identities), keeping with the platform's secret-less posture.
