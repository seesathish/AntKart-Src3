/*
 * AntKart — Kubernetes (hero diagram)
 *
 * Question: how is it orchestrated?
 * A DEPLOYMENT VIEW of the cluster. Sourced from deploy/helm, deploy/argocd and
 * docs/development/3-kubernetes.md. Shows the cluster and its node pool; the namespaces
 * antkart, ingress-nginx, cert-manager and argocd; each deployment with its real replica
 * count; which workloads are ClusterIP-only vs reachable through ingress; TLS
 * termination; and the public path to api.antkart.in.
 *
 * REPLICAS — only ak-products overrides the chart default (replicaCount 2); the chart
 * default is 1, so ak-gateway, ak-cart, ak-order, ak-payments and ak-discount run 1 each.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-kubernetes:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get rough placement.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange the four
 *              namespaces inside the node pool and route the public path down through
 *              ingress-nginx to ak-gateway.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — Kubernetes" "How it is orchestrated: one AKS cluster, four namespaces, the gateway alone exposed" {

    !identifiers hierarchical

    model {

        deploymentEnvironment "Dev" {

            internet = deploymentNode "Internet — api.antkart.in" "GoDaddy A record points the domain at the ingress public IP." "Public" {
                tags "External"
            }

            cluster = deploymentNode "AKS — aks-antkart-dev" "Azure CNI Overlay, OIDC issuer, workload identity." "AKS" "Microsoft Azure - Kubernetes Services" {

                nodepool = deploymentNode "System node pool" "Two nodes." "2 x Standard_D2s_v3" {

                    nsAntkart = deploymentNode "namespace: antkart" "The six services (one generic Helm chart per service)." "namespace" {
                        gateway = infrastructureNode "ak-gateway" "Ocelot. The ONLY workload reachable through ingress; validates the JWT and routes." "Deployment - 1 replica" "Edge"
                        products = infrastructureNode "ak-products" "Product catalogue. ClusterIP only." "Deployment - 2 replicas" "Service"
                        cart = infrastructureNode "ak-cart" "Shopping cart. ClusterIP only." "Deployment - 1 replica" "Service"
                        order = infrastructureNode "ak-order" "Orders and the saga. ClusterIP only." "Deployment - 1 replica" "Service"
                        payments = infrastructureNode "ak-payments" "Payments. ClusterIP only." "Deployment - 1 replica" "Service"
                        discount = infrastructureNode "ak-discount" "Discount gRPC (TCP probes). ClusterIP only." "Deployment - 1 replica" "Service"
                    }

                    nsIngress = deploymentNode "namespace: ingress-nginx" "Public entry point." "namespace" {
                        ingress = infrastructureNode "ingress-nginx controller" "Public LoadBalancer. Terminates TLS and routes /gateway/* to ak-gateway." "Deployment" "Edge"
                    }

                    nsCert = deploymentNode "namespace: cert-manager" "Certificate automation." "namespace" {
                        certmgr = infrastructureNode "cert-manager" "Issues and renews the Let's Encrypt production certificate (secret ak-gateway-tls)." "Deployment" "Edge"
                    }

                    nsArgo = deploymentNode "namespace: argocd" "GitOps controller." "namespace" {
                        argocd = infrastructureNode "Argo CD" "Auto-sync + self-heal (prune false) across six Applications, from Git." "Deployment" "CICD"
                    }
                }
            }
        }

        // The public path into the cluster (top-level identifiers — nesting shows the rest).
        internet -> cluster "HTTPS - ingress-nginx (TLS) - ak-gateway"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        deployment * "Dev" "Kubernetes" "How is it orchestrated? One cluster, four namespaces, only the gateway exposed." {
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
