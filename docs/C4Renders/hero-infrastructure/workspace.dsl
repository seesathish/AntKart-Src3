/*
 * AntKart — Infrastructure as code (hero diagram)
 *
 * Question: how does the cloud get built and rebuilt?
 * Shows Terraform driven by Terragrunt: one root.hcl generating backend / provider /
 * versions into every unit; the eighteen dev units composed from shared modules and
 * grouped by dependency tier; per-unit remote state in a SEPARATE resource group with
 * blob-lease locking; and QA marked planned. Units are grouped by tier, not drawn as
 * eighteen boxes — the detail Mermaid diagram in docs/development/ carries the full graph.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-infrastructure:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get rough placement (root at the top).
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange so root.hcl
 *              fans down into the units, modules sit to one side, and the state backend
 *              stands apart in its own resource group.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — infrastructure as code" "How the cloud is built and rebuilt: Terraform modules composed by Terragrunt units" {

    !identifiers hierarchical

    model {

        iac = softwareSystem "AntKart infrastructure (Terraform + Terragrunt)" "Every Azure resource is provisioned as code for the dev environment." {

            group "Root config (environments/dev)" {
                roothcl = container "root.hcl" "Single place that configures the azurerm backend and shared provider. Generates backend.tf, provider.tf and versions.tf into every unit at init." "Terragrunt" {
                    tags "CICD"
                }
            }

            group "Shared (infrastructure/modules)" {
                modules = container "Terraform modules" "18 environment-agnostic modules describing HOW each resource is built. Reused unchanged across environments." "Terraform" {
                    tags "Infra"
                }
            }

            group "Terragrunt live — environments/dev (18 units)" {
                resourceGroup = container "resource-group" "The root dependency: rg-antkart-dev-eastus. Everything except app-registration traces back to it." "unit" {
                    tags "Infra"
                }
                appReg = container "app-registration" "Independent — manages a directory object via the azuread provider, not a resource-group resource." "unit" {
                    tags "Identity"
                }
                dataUnits = container "Data-store units" "cosmosdb · postgresql · redis. Each depends only on resource-group." "3 units" {
                    tags "Datastore"
                }
                msgUnits = container "Messaging units" "servicebus · eventgrid. Each depends only on resource-group." "2 units" {
                    tags "Infra"
                }
                platformUnits = container "Platform units" "networking · container-registry · key-vault · observability · communication-services · governance." "6 units" {
                    tags "Infra"
                }
                aks = container "aks" "The cluster. Depends on resource-group, networking, container-registry and observability." "unit" {
                    tags "Infra"
                }
                functionApp = container "function-app" "Serverless notifications host. Depends on resource-group and observability." "unit" {
                    tags "Infra"
                }
                githubOidc = container "github-oidc" "Federated credential for CI/CD (id-ak-cicd-dev, AcrPush). Depends on resource-group and container-registry." "unit" {
                    tags "Identity"
                }
                workloadIdentity = container "workload-identity" "Per-service federated identities. Depends on aks (OIDC issuer), key-vault, servicebus and eventgrid." "unit" {
                    tags "Identity"
                }
                roleAssignments = container "role-assignments" "Grants the function-app identity its data-plane roles. Depends on function-app, key-vault, servicebus and eventgrid." "unit" {
                    tags "Identity"
                }
            }

            group "Remote state (separate resource group)" {
                stateBackend = container "Azure Storage — rg-antkart-tfstate" "Per-unit state blob (key = unit path). Blob-lease locking serialises applies. Deliberately separate from the app resource group, so an app teardown never removes state." "azurerm backend" {
                    tags "Datastore"
                }
            }

            group "Promotion" {
                qa = container "environments/qa" "Planned — a second live tree reusing the same modules with QA inputs. Not yet created; must use a distinct backend key prefix to avoid colliding with dev state." "planned" {
                    tags "Planned"
                }
            }
        }

        // root.hcl generates the backend / provider / versions into every unit (shown
        // to representative tiers to keep the fan-out legible) and configures the backend.
        iac.roothcl -> iac.resourceGroup "Generates backend / provider / versions (into every unit)"
        iac.roothcl -> iac.aks "Generates config"
        iac.roothcl -> iac.workloadIdentity "Generates config"
        iac.roothcl -> iac.stateBackend "Configures the azurerm backend"

        // Units compose from the shared modules (representative arrows).
        iac.resourceGroup -> iac.modules "Composes"
        iac.aks -> iac.modules "Composes"
        iac.dataUnits -> iac.modules "Compose"

        // Dependency tiers — resource-group is the hub; the two composite tails carry
        // the interesting order.
        iac.dataUnits -> iac.resourceGroup "Depends on"
        iac.msgUnits -> iac.resourceGroup "Depends on"
        iac.platformUnits -> iac.resourceGroup "Depends on"
        iac.aks -> iac.resourceGroup "Depends on"
        iac.aks -> iac.platformUnits "Needs networking / registry / observability"
        iac.workloadIdentity -> iac.aks "Needs the OIDC issuer from"
        iac.roleAssignments -> iac.functionApp "Grants roles to"

        // Per-unit remote state (representative arrow).
        iac.resourceGroup -> iac.stateBackend "Per-unit state (blob lease lock)"

        // Promotion reuses the same modules.
        iac.qa -> iac.modules "Reuses (planned)" {
            tags "Planned"
        }
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container iac "InfrastructureAsCode" "How does the cloud get built and rebuilt? Terragrunt units composed from shared modules, one root config, per-unit state." {
            include *
            // PHASE ONE: autoLayout is ON. Before hand-arranging, COMMENT OUT the next
            // line, refresh, then drag. Leave it commented once you start dragging.
            autoLayout tb 300 150
        }

        styles {
            // Hide descriptions. Keep metadata — that is the technology line.
            element "Element" {
                description true
                metadata true
            }
            element "Group" {
                strokeWidth 4
                color #5F5E5A
                fontSize 24
            }
            element "Person" {
                shape Person
                background #5F5E5A
                color #ffffff
                fontSize 26
            }
            element "Software System" {
                background #888780
                color #ffffff
            }
            element "External" {
                background #888780
                color #ffffff
            }
            element "Identity" {
                background #BA7517
                color #ffffff
            }
            element "Managed" {
                background #378ADD
                color #ffffff
            }
            element "Service" {
                background #1D9E75
                color #ffffff
                shape RoundedBox
            }
            element "Serverless" {
                background #0F6E56
                color #ffffff
                shape RoundedBox
            }
            element "Datastore" {
                background #185FA5
                color #ffffff
                shape Cylinder
            }
            element "Edge" {
                background #7F77DD
                color #ffffff
                shape RoundedBox
            }
            element "CICD" {
                background #639922
                color #ffffff
                shape RoundedBox
            }
            element "Infra" {
                background #888780
                color #ffffff
            }
            element "Planned" {
                background #5F5E5A
                color #ffffff
                border dashed
                strokeWidth 3
            }
            element "Issue" {
                background #E24B4A
                color #ffffff
                border dashed
                strokeWidth 3
            }
            relationship "Relationship" {
                dashed false
                thickness 2
                fontSize 22
                routing Orthogonal
            }
            relationship "Planned" {
                dashed true
                thickness 2
                fontSize 22
                routing Orthogonal
            }
        }
    }
}
