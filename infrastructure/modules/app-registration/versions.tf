# =============================================================================
# App Registration module — provider requirements
# =============================================================================
# This module talks to Microsoft Entra ID (the DIRECTORY) via Microsoft Graph,
# NOT to the Azure resource manager. That is a different provider: azuread,
# rather than the azurerm used by every resource module so far.
terraform {
  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }
}

# The azuread provider authenticates with the same identity Terraform already
# uses (the ARM_* environment variables resolve for azuread too). No extra
# configuration is needed here; an empty block uses that context.
#
# IMPORTANT: creating directory objects (app registrations) needs DIRECTORY
# permissions, which are separate from the subscription RBAC roles the
# automation identity holds. See the guide's Execute section if apply fails with
# an insufficient-privileges / Graph error.
provider "azuread" {}
