/*
 * AntKart — Azure services (hero diagram)
 *
 * Question: where does it run, and what replaced what?
 * A DEPLOYMENT VIEW of the managed estate — subscription, region, resource group,
 * virtual network, AKS, and the managed services. Sourced from the Terragrunt units
 * (infrastructure/environments/dev) and deploy/helm. Each element notes which Phase 1
 * component it replaced where one did. API Management is marked planned.
 *
 * NOTE ON REGION — the repository provisions a SINGLE region, East US (eastus), in the
 * resource group rg-antkart-dev-eastus. There is no East US 2 in the code, so only one
 * region is drawn; a second region/QA is planned (see hero-infrastructure).
 *
 * NOTE ON TAGS — every Azure element carries a "Microsoft Azure - ..." theme tag so the
 * icons render from the microsoft-azure-2021.01.26 theme. If any icon does not appear,
 * the tag string does not match the theme exactly — adjust that one string; the box
 * still renders either way.
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

workspace "AntKart — Azure services" "Where it runs: the managed Azure estate, single region East US" {

    !identifiers hierarchical

    model {

        deploymentEnvironment "Dev" {

            entra = deploymentNode "Microsoft Entra ID" "Tenant-level identity, not inside the resource group. Issues the tokens every service validates. Replaced Keycloak." "OAuth2 / OIDC" "Identity"

            subscription = deploymentNode "Azure Subscription" "Single subscription for the dev environment." "Azure" "Microsoft Azure - Subscriptions" {

                region = deploymentNode "East US (eastus)" "Single region. A second region / QA is planned, not built." "Azure region" {

                    rg = deploymentNode "rg-antkart-dev-eastus" "Application resource group. Terraform state lives in a SEPARATE resource group (rg-antkart-tfstate)." "Resource group" "Microsoft Azure - Resource Groups" {

                        vnet = deploymentNode "vnet-antkart-dev-eastus" "Virtual network — aks, private-endpoints and gateway subnets." "VNet" "Microsoft Azure - Virtual Networks" {
                            aks = deploymentNode "AKS — aks-antkart-dev" "Azure CNI Overlay, OIDC issuer, workload identity. Node pool: 2 x Standard_D2s_v3. Runs the six services and the serverless wiring." "AKS" "Microsoft Azure - Kubernetes Services"
                        }

                        cosmos = infrastructureNode "Cosmos DB (MongoDB API)" "Product catalogue. Replaced local MongoDB." "antkart-products" "Microsoft Azure - Azure Cosmos DB"
                        postgres = infrastructureNode "PostgreSQL Flexible Server" "Order, Payments and Discount databases." "AKOrdersDb / AKPaymentsDb / AKDiscountDb" "Microsoft Azure - Azure Database PostgreSQL Server"
                        redis = infrastructureNode "Azure Managed Redis" "Shopping cart store. Replaced local Redis." "AKCart:cart:{userId}" "Microsoft Azure - Azure Cache Redis"
                        servicebus = infrastructureNode "Azure Service Bus" "Orchestrated SAGA transport (MassTransit, Entra auth). Replaced RabbitMQ." "Service Bus namespace" "Microsoft Azure - Azure Service Bus"
                        eventgrid = infrastructureNode "Event Grid" "Fire-and-forget notification events." "evgt-antkart-dev" "Microsoft Azure - Event Grid Topics"
                        functions = infrastructureNode "Azure Functions" "Serverless notification handlers. Replaced the notification microservice." "func-antkart-notifications-dev" "Microsoft Azure - Function Apps"
                        keyvault = infrastructureNode "Key Vault" "Secrets, read at runtime via managed identity." "RBAC data plane" "Microsoft Azure - Key Vaults"
                        acs = infrastructureNode "Azure Communication Services" "Transactional customer email. Replaced Mailhog." "Email" "Microsoft Azure - Azure Communication Services"
                        acr = infrastructureNode "Container Registry" "Service container images." "acrantkartdev" "Microsoft Azure - Container Registries"
                        appInsights = infrastructureNode "Application Insights" "Telemetry collection for the services and Functions." "Azure Monitor" "Microsoft Azure - Application Insights"
                        logAnalytics = infrastructureNode "Log Analytics" "Central log store, queried with KQL." "log-antkart-dev" "Microsoft Azure - Log Analytics Workspaces"
                        apim = infrastructureNode "API Management" "PLANNED managed edge (ADR-020) — not yet provisioned." "planned" "Planned"
                    }
                }
            }
        }
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        deployment * "Dev" "AzureServices" "Where does it run, and what replaced what? The managed Azure estate in a single region." {
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
