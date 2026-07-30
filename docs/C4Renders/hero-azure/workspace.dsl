/*
 * AntKart — Azure services (hero diagram)
 *
 * Question: where does it run, and what replaced what?
 * A DEPLOYMENT VIEW of the managed estate — subscription, ONE resource group spanning
 * TWO regions, virtual network, AKS, and the managed services. Sourced from the
 * Terragrunt units (infrastructure/environments/dev) and deploy/helm. Each element notes
 * which Phase 1 component it replaced where one did. API Management is marked planned.
 *
 * NOTE ON REGION — the repository provisions TWO regions inside the SAME resource group
 * (rg-antkart-dev-eastus, a logical container that is not region-bound). East US holds
 * AKS and everything else; East US 2 holds PostgreSQL Flexible Server and Azure Managed
 * Redis. PostgreSQL is in East US 2 because East US is offer-restricted for Flexible
 * Server (infrastructure/environments/dev/postgresql/terragrunt.hcl:46); Redis is
 * colocated with it there (redis/terragrunt.hcl:46). Data calls from the cluster to
 * those two stores CROSS the region boundary — drawn as two arrows.
 *
 * NOTE ON TAGS — Azure elements carry a "Microsoft Azure - ..." tag whose string is
 * VERIFIED against the microsoft-azure-2021.01.26 theme.json, so the icons render. Three
 * elements deliberately carry NO Azure tag (styled generic boxes instead): the
 * subscription / resource-group / vnet BOUNDARIES (Infra), Azure Communication Services
 * (this theme has no ACS tag), and Log Analytics (no verified tag). An unmatched theme
 * tag renders no icon and looks like a bug, so those are left as clean styled boxes.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-azure:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get rough placement.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange the managed
 *              services around the resource-group boundary and pull Entra out to one side.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — Azure services" "Where it runs: the managed Azure estate across East US and East US 2" {

    !identifiers hierarchical

    model {

        deploymentEnvironment "Dev" {

            entra = deploymentNode "Microsoft Entra ID" "Tenant-level identity, not inside the resource group. Issues the tokens every service validates. Replaced Keycloak." "OAuth2 / OIDC" "Microsoft Azure - Azure Active Directory"

            subscription = deploymentNode "Azure Subscription" "Single subscription for the dev environment." "Azure" "Infra" {

                // ONE resource group, TWO regions. A resource group is a LOGICAL container
                // and is not region-bound, so both regions sit inside this single group —
                // there is no second resource group.
                rg = deploymentNode "rg-antkart-dev-eastus" "Application resource group — a logical, NOT region-bound container. Terraform state lives in a SEPARATE resource group (rg-antkart-tfstate)." "Resource group" "Infra" {

                    eastus = deploymentNode "Region: East US (eastus)" "Primary region — AKS and every managed service except the two offer-restricted data stores." "Azure region" {

                        vnet = deploymentNode "vnet-antkart-dev-eastus" "Virtual network — aks, private-endpoints and gateway subnets." "VNet" "Infra" {
                            aks = deploymentNode "AKS — aks-antkart-dev" "Azure CNI Overlay, OIDC issuer, workload identity. Node pool: 2 x Standard_D2s_v3. Runs the six services and the serverless wiring." "AKS" "Microsoft Azure - Kubernetes Services"
                        }

                        cosmos = infrastructureNode "Cosmos DB (MongoDB API)" "Product catalogue. Replaced local MongoDB." "antkart-products" "Microsoft Azure - Azure Cosmos DB"
                        servicebus = infrastructureNode "Azure Service Bus" "Orchestrated SAGA transport (MassTransit, Entra auth). Replaced RabbitMQ." "Service Bus namespace" "Microsoft Azure - Service Bus"
                        eventgrid = infrastructureNode "Event Grid" "Fire-and-forget notification events." "evgt-antkart-dev" "Microsoft Azure - Event Grid Topics"
                        functions = infrastructureNode "Azure Functions" "Serverless notification handlers. Replaced the notification microservice." "func-antkart-notifications-dev" "Microsoft Azure - Function Apps"
                        keyvault = infrastructureNode "Key Vault" "Secrets, read at runtime via managed identity." "RBAC data plane" "Microsoft Azure - Key Vaults"
                        acs = infrastructureNode "Azure Communication Services" "Transactional customer email. Replaced Mailhog. (No icon — this theme has no ACS tag.)" "Email" "Managed"
                        acr = infrastructureNode "Container Registry" "Service container images." "acrantkartdev" "Microsoft Azure - Container Registries"
                        appInsights = infrastructureNode "Application Insights" "Telemetry collection for the services and Functions." "Azure Monitor" "Microsoft Azure - Application Insights"
                        logAnalytics = infrastructureNode "Log Analytics" "Central log store, queried with KQL. (No icon — no verified Log Analytics tag in this theme.)" "log-antkart-dev" "Managed"
                        apim = infrastructureNode "API Management" "PLANNED managed edge (ADR-020) — not yet provisioned." "planned" "Microsoft Azure - API Management Services,Planned"
                    }

                    eastus2 = deploymentNode "Region: East US 2 (eastus2)" "Paired region — holds the two data stores that could not be provisioned in East US." "Azure region" {
                        postgres = infrastructureNode "PostgreSQL Flexible Server" "Order, Payments and Discount databases. Provisioned in East US 2 because East US is offer-restricted for Flexible Server." "AKOrdersDb / AKPaymentsDb / AKDiscountDb" "Microsoft Azure - Azure Database PostgreSQL Server"
                        redis = infrastructureNode "Azure Managed Redis" "Shopping cart store. Colocated with PostgreSQL in East US 2." "AKCart:cart:{userId}" "Microsoft Azure - Cache Redis"
                    }
                }
            }

            // Cross-region data calls — these two arrows leave the East US boundary and
            // enter East US 2, so a reader sees that the cluster's data hop crosses regions.
            subscription.rg.eastus.vnet.aks -> subscription.rg.eastus2.postgres "Order / Payments / Discount data (crosses region)"
            subscription.rg.eastus.vnet.aks -> subscription.rg.eastus2.redis "Cart data (crosses region)"
        }
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        deployment * "Dev" "AzureServices" "Where does it run, and what replaced what? The managed Azure estate across East US and East US 2." {
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
                background #D9D8D4
                color #5F5E5A
                border dashed
                strokeWidth 3
            }
            element "Issue" {
                background #ffffff
                color #E24B4A
                stroke #E24B4A
                strokeWidth 4
                border dashed
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
