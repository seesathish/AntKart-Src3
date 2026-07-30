/*
 * AntKart — System overview (hero diagram)
 *
 * Answers one question: what is this thing, who uses it, and what does it depend on?
 * Deliberately excludes databases, message brokers, ingress, cert-manager and Argo CD.
 * Those belong to the Azure, Kubernetes and DevOps diagrams.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-system:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TARGET LAYOUT (left to right)
 *   Users  ->  API gateway  ->  Services  ->  Razorpay and Communication Services
 *   Entra sits ABOVE the gateway. Communication Services sends email back to the user,
 *   which closes the loop on the right.
 *
 * IMPORTANT — autoLayout cannot produce that arrangement. It ranks elements by
 * relationship direction, so Entra will land to the RIGHT of the gateway rather than
 * above it, and the right-hand column will not hold its position. Use autoLayout only
 * to get the left-to-right flow and rough spacing, then:
 *
 *   1. COMMENT OUT the autoLayout line below
 *   2. Refresh the browser
 *   3. Drag Entra above the gateway, pull Razorpay and Communication Services to the
 *      right edge, and stack the five services top to bottom
 *
 * Placement autosaves into workspace.json in this folder every five seconds. While
 * autoLayout is present the engine recalculates on every load and discards your work,
 * so once you start dragging, leave it commented out permanently.
 */

workspace "AntKart — system overview" "What the platform is, who uses it, and what it depends on" {

    !identifiers hierarchical

    model {

        // ── Left: the people ──────────────────────────────────────────────────
        group "Users" {
            customer = person "Customer" "Browses the catalogue, builds a cart, places orders and pays." {
                tags "Person"
            }
            administrator = person "Administrator" "Manages the catalogue and order status. Holds the admin app role." {
                tags "Person"
            }
        }

        // ── Above the gateway: identity ───────────────────────────────────────
        group "Identity" {
            entra = softwareSystem "Microsoft Entra ID" "Issues the delegated access tokens the platform validates on every request." {
                tags "Identity"
            }
        }

        // ── Centre: the platform ──────────────────────────────────────────────
        antkart = softwareSystem "AntKart" "Cloud-native .NET 9 e-commerce platform on Azure Kubernetes Service, reachable at https://api.antkart.in" {

            group "Edge" {
                gateway = container "API gateway" "Single entry point. Validates the bearer token and routes each path to the service that owns it." "Ocelot · .NET 9" {
                    tags "Service"
                }
            }

            // Declared in the order they should stack, top to bottom.
            group "Services" {
                products = container "Products" "Product catalogue, enriched with discounts and reserving stock when an order is placed." "Minimal API · .NET 9" {
                    tags "Service"
                }
                discount = container "Discount" "Discount lookup by SKU. gRPC only, never reachable from outside the cluster." "gRPC · .NET 9" {
                    tags "Service"
                }
                cart = container "Shopping cart" "Cart operations. The user identity comes from the token, never from the URL." "Minimal API · .NET 9" {
                    tags "Service"
                }
                order = container "Order" "Order lifecycle and the orchestrated saga that reserves stock and reacts to payment." "Minimal API · .NET 9" {
                    tags "Service"
                }
                payments = container "Payments" "Payment initiation and signature verification." "Minimal API · .NET 9" {
                    tags "Service"
                }
                notifications = container "Notifications" "Serverless handlers that turn platform events into customer email." "Azure Functions · .NET 9" {
                    tags "Serverless"
                }
            }
        }

        // ── Right: what the platform reaches out to ───────────────────────────
        group "External services" {
            razorpay = softwareSystem "Razorpay" "Payment gateway. Creates payment orders and defines the signature the platform verifies." {
                tags "External"
            }
            acs = softwareSystem "Azure Communication Services" "Delivers transactional email to customers." {
                tags "Managed"
            }
        }

        // GoDaddy DNS and Let's Encrypt removed — DNS and certificates are a
        // Kubernetes concern, not part of "what is this thing". To restore, add:
        //   godaddy = softwareSystem "GoDaddy DNS" "..." { tags "External" }
        //   letsencrypt = softwareSystem "Let's Encrypt" "..." { tags "External" }
        //   customer -> godaddy "Resolves api.antkart.in"
        //   antkart -> letsencrypt "TLS certificate"

        // ── Relationships ─────────────────────────────────────────────────────
        // Technology shown only where it is an architectural fact worth reading.
        customer -> antkart.gateway "Shops and pays"
        administrator -> antkart.gateway "Manages catalogue"

        antkart.gateway -> entra "Validates the access token" "OpenID Connect"

        antkart.gateway -> antkart.products "Catalogue requests"
        antkart.gateway -> antkart.cart "Cart requests"
        antkart.gateway -> antkart.order "Order requests"
        antkart.gateway -> antkart.payments "Payment requests"
        antkart.products -> antkart.discount "Discount by SKU" "gRPC"

        antkart.order -> antkart.products "Reserves stock"
        antkart.payments -> antkart.order "Payment outcome"
        antkart.order -> antkart.notifications "Order events"
        antkart.payments -> antkart.notifications "Payment events"

        antkart.payments -> razorpay "Creates and verifies payments" "HTTPS"
        antkart.notifications -> acs "Sends customer email" "Managed identity"

        // Closes the loop on the right: the platform reaches the customer again.
        acs -> customer "Delivers order and payment email"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container antkart "SystemOverview" "What is AntKart, who uses it, and what does it depend on?" {
            include *
            //autoLayout lr 300 150
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
            relationship "Relationship" {
                dashed false
                thickness 2
                fontSize 22
            }
        }
    }
}
