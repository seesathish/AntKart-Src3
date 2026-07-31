/*
 * AntKart — Security (hero diagram)
 *
 * Question: how is it secured, end to end?
 * Sourced from ADR-016, ADR-022, docs/guides/identity-concepts.md,
 * docs/development/6-security.md and docs/KNOWN_ISSUES.md. Shows the secret-less chain
 * grouped by trust boundary: Public, Cluster (ClusterIP-only), and the Azure control
 * plane. KI-002 (Discount decodes the JWT without cryptographic validation) is included
 * as a clearly marked known gap with its own tag.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-security:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get rough placement.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange the three
 *              trust boundaries left to right: Public → Cluster → Azure control plane.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — security" "How it is secured end to end: no stored secrets, defence in depth on tokens, trust boundaries" {

    !identifiers hierarchical

    model {

        client = person "Client" "Signs in with Entra (OAuth2 auth-code + PKCE), then calls the API with a bearer token." {
            tags "Person"
        }

        group "Public — internet-facing" {
            entra = softwareSystem "Microsoft Entra ID" "Issues delegated access tokens the platform validates. Replaced Keycloak." {
                tags "Identity"
            }
            ingress = softwareSystem "ingress-nginx + cert-manager" "TLS termination at the edge — Let's Encrypt production certificate for api.antkart.in." {
                tags "Edge"
            }
            gateway = softwareSystem "API gateway" "Validates the Entra JWT at the edge, then routes to the internal services." {
                tags "Edge"
            }
        }

        group "Cluster — ClusterIP-only" {
            rest = softwareSystem "REST services" "Products, Order, Payments, Cart. Each RE-validates the JWT — defence in depth, not trusting the gateway." {
                tags "Service"
            }
            discount = softwareSystem "Discount (gRPC)" "KI-002: decodes the JWT but does NOT verify signature / issuer / audience. Mitigated — ClusterIP-only, reached only by Products." {
                tags "Issue"
            }
        }

        group "Azure control plane — secret-less identity" {
            uami = softwareSystem "Per-service workload identities" "Six user-assigned managed identities, each federated to the AKS OIDC issuer for its ServiceAccount. No stored secret." {
                tags "Identity"
            }
            keyvault = softwareSystem "Key Vault" "Secrets read at runtime via managed identity (Key Vault Secrets User)." {
                tags "Managed"
            }
            rbac = softwareSystem "Data-plane RBAC" "Least-privilege role assignments scoped per resource — Service Bus / Event Grid / Key Vault data roles." {
                tags "Identity"
            }
            cicd = softwareSystem "GitHub OIDC (CI/CD)" "id-ak-cicd-dev federated credential, AcrPush only. No stored cloud secret." {
                tags "CICD"
            }
            acr = softwareSystem "Container Registry" "acrantkartdev. Images pushed by CI/CD, pulled by the kubelet." {
                tags "Managed"
            }
        }

        // ── The chain ─────────────────────────────────────────────────────────
        client -> ingress "HTTPS · api.antkart.in"
        ingress -> gateway "Forwards after TLS termination"
        gateway -> entra "validate-jwt" "OpenID Connect"
        gateway -> rest "Routes (token re-validated)"
        rest -> discount "gRPC — KI-002: no cryptographic validation"
        rest -> uami "Runs as"
        uami -> keyvault "Reads secrets (RBAC)"
        uami -> rbac "Least-privilege scoped by"
        cicd -> acr "AcrPush (no stored secret)"
        rest -> acr "Image pulled (kubelet AcrPull)"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        systemLandscape "Security" "How is it secured end to end? Trust boundaries: Public, Cluster, Azure control plane — with KI-002 marked." {
            include *
            // PHASE ONE: autoLayout is ON. Before hand-arranging, COMMENT OUT the next
            // line, refresh, then drag. Leave it commented once you start dragging.
            autoLayout lr 300 150
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
