/*
 * AntKart — Security (hero diagram)
 *
 * Question: how is it secured, end to end?
 *
 * Two chains, no stored secrets in either.
 *
 *   CALLER   The client signs in with Entra and receives an access token. The request
 *            arrives over TLS terminated at the edge. The gateway validates the token
 *            against Entra's published signing keys, then routes inward; every service
 *            validates it again rather than trusting the gateway.
 *
 *   WORKLOAD The cluster projects a ServiceAccount token into each pod, signed by the
 *            cluster's own OIDC issuer. The service presents that token to Entra, which
 *            matches the issuer and subject against a federated credential and grants the
 *            service's managed identity. That identity holds scoped data-plane roles.
 *            Nothing in the cluster stores a credential.
 *
 * Two registry paths sit outside both chains: CI/CD pushes with AcrPush, the kubelet
 * pulls with AcrPull. Neither uses a registry password.
 *
 * KI-002 is drawn as a known gap: Discount decodes the JWT without verifying its
 * signature, issuer or audience. It is ClusterIP-only and reached only by Products.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-security:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get rough placement.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange.
 *              LAYOUT HINT — Entra is referenced from three places (the client signing
 *              in, the gateway validating, the services exchanging). Place it top-centre
 *              rather than at the far left, or those arrows will double back across the
 *              diagram. Caller chain along the top, workload chain below it, the two
 *              registry arrows off to one side.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — security" "Two authentication chains, neither storing a secret: the caller's token and the workload's federated identity" {

    !identifiers hierarchical

    model {

        client = person "Client" "Signs in with Entra using OAuth2 authorization code with PKCE." {
            tags "Person"
        }

        entra = softwareSystem "Microsoft Entra ID" "Issues access tokens to callers, publishes the keys those tokens are validated against, and grants managed identities to workloads presenting a trusted federated token." {
            tags "Identity"
        }

        group "Edge" {
            ingress = softwareSystem "ingress-nginx + cert-manager" "Terminates TLS. cert-manager holds a Let's Encrypt certificate and renews it." {
                tags "Edge"
            }
            gateway = softwareSystem "API gateway" "The only service reachable from outside. Validates the caller's token against Entra's published signing keys before routing." {
                tags "Edge"
            }
        }

        group "Cluster" {
            oidcIssuer = softwareSystem "AKS OIDC issuer" "Signs a short-lived ServiceAccount token and projects it into each pod. Entra is configured to trust this issuer." {
                tags "Identity"
            }
            rest = softwareSystem "Services" "Products, Cart, Order, Payments. ClusterIP-only. Each validates the caller's token again rather than trusting the gateway." {
                tags "Service"
            }
            discount = softwareSystem "Discount (gRPC)" "KI-002 — decodes the caller's token but does not verify its signature, issuer or audience. ClusterIP-only and reached only by Products." {
                tags "Issue"
            }
            kubelet = softwareSystem "Kubelet identity" "The cluster's own identity. Pulls images; holds no registry password." {
                tags "Identity"
            }
        }

        group "Azure" {
            uami = softwareSystem "Managed identities" "One user-assigned identity per service, each with a federated credential naming the cluster issuer and that service's ServiceAccount." {
                tags "Identity"
            }
            keyvault = softwareSystem "Key Vault" "Holds every connection string and API key. Read at startup; nothing is stored in the cluster." {
                tags "Managed"
            }
            azure = softwareSystem "Service Bus, Event Grid, data stores" "Reached with the identity's token. Roles are scoped per resource, not per subscription." {
                tags "Managed"
            }
            acr = softwareSystem "Container Registry" "Images pushed by CI/CD, pulled by the kubelet." {
                tags "Managed"
            }
            cicd = softwareSystem "GitHub Actions" "Authenticates with a federated credential naming the repository and environment. No cloud secret is stored in GitHub." {
                tags "CICD"
            }
        }

        // Caller chain.
        client -> entra "Signs in; receives an access token"
        client -> ingress "HTTPS, bearer token"
        ingress -> gateway "Forwards after TLS termination"
        gateway -> entra "Fetches the signing keys"
        gateway -> rest "Routes; the token is validated again"
        rest -> discount "gRPC"

        // Workload chain.
        oidcIssuer -> rest "Projects a signed ServiceAccount token"
        rest -> entra "Presents it — no stored secret"
        entra -> uami "Matches issuer and subject to a federated credential"
        uami -> keyvault "Key Vault Secrets User"
        uami -> azure "Scoped data-plane roles"

        // Registry.
        cicd -> acr "AcrPush"
        kubelet -> acr "AcrPull"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        systemLandscape "Security" "The caller's token is validated at the edge and again inside. The workload's identity is federated to the cluster, so no credential is stored anywhere." {
            include *
            // PHASE ONE: autoLayout is ON. Before hand-arranging, COMMENT OUT the next
            // line, refresh, then drag. Leave it commented once you start dragging.
            //autoLayout lr 150 120
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
