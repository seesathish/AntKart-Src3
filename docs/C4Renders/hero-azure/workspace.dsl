/*
 * AntKart — Azure services (hero diagram)
 *
 * Question: where does it run, and how does a request move through the estate?
 *
 * Microsoft reference-architecture style: small bold boxes, service icons, and arrows
 * carrying ONLY a step number. The numbered explanation lives under the diagram in
 * README.md — that is what keeps the picture calm.
 *
 * ONE boundary only: the cluster. Functional grouping (compute / data / messaging) was
 * tried and removed — it fought the left-to-right flow instead of carrying it.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-azure:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *
 * TWO-PHASE: autoLayout ON for rough ranks, then COMMENT IT OUT before dragging.
 * Target layout, left to right:
 *   Customer → API Management → [cluster box: gateway, then five services stacked]
 *   → data stores and messaging in one parallel column → Functions → email.
 *   Entra sits above the cluster. Nothing else floats.
 */

workspace "AntKart — Azure services" "Where it runs, and the path a request takes" {

    !identifiers hierarchical

    model {

        customer = person "Customer" "" {
            tags "Person"
        }

        azure = softwareSystem "AntKart on Microsoft Azure" "" {

            apim = container "API Management" "" "Planned" {
                tags "Planned"
            }

            entra = container "Microsoft Entra ID" "" "Identity" {
                tags "Microsoft Azure - Azure Active Directory"
            }

            group "Azure Kubernetes Service" {
                gateway = container "Gateway" "" "Ocelot" {
                    tags "Edge"
                }
                products = container "Products" "" ".NET 9" {
                    tags "Service"
                }
                discount = container "Discount" "" "gRPC" {
                    tags "Service"
                }
                cart = container "Cart" "" ".NET 9" {
                    tags "Service"
                }
                order = container "Order" "" ".NET 9" {
                    tags "Service"
                }
                payments = container "Payments" "" ".NET 9" {
                    tags "Service"
                }
            }

            cosmos = container "Cosmos DB" "" "Mongo API" {
                tags "Microsoft Azure - Azure Cosmos DB"
            }
            redis = container "Redis" "" "East US 2" {
                tags "Microsoft Azure - Cache Redis"
            }
            postgres = container "PostgreSQL" "" "East US 2" {
                tags "Microsoft Azure - Azure Database PostgreSQL Server"
            }
            servicebus = container "Service Bus" "" "Topics" {
                tags "Microsoft Azure - Service Bus"
            }
            eventgrid = container "Event Grid" "" "Topics" {
                tags "Microsoft Azure - Event Grid Topics"
            }
            functions = container "Functions" "" "Serverless" {
                tags "Microsoft Azure - Function Apps"
            }
            acs = container "Communication Services" "" "Email" {
                tags "Managed"
            }
        }

        // ── Numbered path. The legend lives in README.md ──────────────────────
        customer -> azure.apim "1"
        azure.apim -> azure.gateway "2"

        azure.gateway -> azure.products "3"
        azure.gateway -> azure.cart "3"
        azure.gateway -> azure.order "3"
        azure.gateway -> azure.payments "3"

        azure.products -> azure.discount "4"
        azure.products -> azure.cosmos "5"
        azure.cart -> azure.redis "6"
        azure.order -> azure.postgres "7"
        azure.payments -> azure.postgres "7"
        azure.discount -> azure.postgres "7"

        azure.order -> azure.servicebus "8"
        azure.payments -> azure.servicebus "8"
        azure.order -> azure.eventgrid "9"
        azure.payments -> azure.eventgrid "9"

        azure.eventgrid -> azure.functions "10"
        azure.functions -> azure.acs "11"

        azure.entra -> azure.gateway "identity"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container azure "AzureServices" "Where does it run, and how does a request move through it?" {
            include *
            // PHASE ONE. Comment out before hand-arranging, and leave it commented.
            //autoLayout lr 150 100
        }

        styles {
            element "Element" {
                description false
                metadata true
                fontSize 30
                width 480
                height 260
            }
            element "Group" {
                strokeWidth 6
                color #5F5E5A
                fontSize 34
            }
            element "Person" {
                shape Person
                background #5F5E5A
                color #ffffff
            }
            element "Software System" {
                background #888780
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
            element "Edge" {
                background #7F77DD
                color #ffffff
                shape RoundedBox
            }
            element "Planned" {
                background #5F5E5A
                color #ffffff
                border dashed
                strokeWidth 4
            }
// Azure-tagged elements take their fill from the theme, which is near-white
            // and vanishes on a light export. These overrides give each a solid fill
            // from the locked palette. Icons still come from the theme.
            element "Microsoft Azure - Azure Cosmos DB" {
                background #185FA5
                color #ffffff
            }
            element "Microsoft Azure - Azure Database PostgreSQL Server" {
                background #185FA5
                color #ffffff
            }
            element "Microsoft Azure - Cache Redis" {
                background #185FA5
                color #ffffff
            }
            element "Microsoft Azure - Service Bus" {
                background #378ADD
                color #ffffff
            }
            element "Microsoft Azure - Event Grid Topics" {
                background #378ADD
                color #ffffff
            }
            element "Microsoft Azure - Function Apps" {
                background #0F6E56
                color #ffffff
            }
            element "Microsoft Azure - Azure Active Directory" {
                background #BA7517
                color #ffffff
            }
            relationship "Relationship" {
                dashed false
                thickness 4
                fontSize 40
                routing Orthogonal
            }
        }
    }
}
