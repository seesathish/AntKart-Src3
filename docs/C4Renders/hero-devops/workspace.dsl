/*
 * AntKart — DevOps (hero diagram)
 *
 * Question: how does a commit become a running pod?
 *
 * THE SHAPE — one straight left-to-right story, three numbered beats. The GitHub
 * repository (pull request + master) sits at the left. The verbs and the detail live
 * in the README legend under the image; the diagram itself stays light.
 *
 *   Developer opens a pull request, then:
 *     1  Branch protection — the required checks must pass, then the code merges.
 *     2  The container image is rebuilt and pushed (immutable commit-SHA tag). CD
 *        authenticates to Azure with an Entra OIDC federated credential — no secret.
 *     3  Argo CD (GitOps) reads master and updates the pods on AKS. Nothing is pushed
 *        to the cluster — Argo pulls, and the kubelet pulls the image.
 *
 * The three CI checks (build/test, SonarCloud, Trivy) are deliberately NOT drawn as
 * separate boxes — they are "4 required checks" inside Branch protection, and the
 * legend spells them out. That is what keeps this diagram from getting heavy.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-devops:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *
 * TWO-PHASE: autoLayout ON for rough ranks, then COMMENT IT OUT before dragging.
 * Target layout (left to right): Developer, then the GitHub repository box (pull
 * request above master), Branch protection just right of the repo returning into
 * master, then Container image, then Argo CD and AKS pods at the far right. Entra ID
 * sits below/beside the Container image, its arrow feeding the push. The image -> pods
 * pull arrow and the Argo -> master read arrow point back at their source on purpose.
 */

workspace "AntKart — DevOps" "How a commit becomes a running pod: branch-protected merge, secret-less image build, pull-based delivery" {

    !identifiers hierarchical

    model {

        developer = person "Developer" "" {
            tags "Person"
        }

        delivery = softwareSystem "AntKart delivery" "" {

            group "GitHub repository" {
                pr = container "Pull request" "" "feature branch" {
                    tags "CICD"
                }
                master = container "master" "" "source of truth" {
                    tags "CICD"
                }
            }

            protection = container "Branch protection" "" "4 required checks" {
                tags "Edge"
            }

            image = container "Container image" "" "Registry · commit-SHA tag" {
                tags "Microsoft Azure - Container Registries"
            }
            entra = container "Microsoft Entra ID" "" "OIDC federated · no secret" {
                tags "Microsoft Azure - Azure Active Directory"
            }

            argo = container "Argo CD" "" "GitOps · auto-sync" {
                tags "Edge"
            }
            aks = container "AKS pods" "" "aks-antkart-dev" {
                tags "Microsoft Azure - Kubernetes Services"
            }
        }

        // Developer opens the PR (the setup — unnumbered).
        developer -> delivery.pr ""

        // 1 — branch protection, then merge.
        delivery.pr -> delivery.protection "1"
        delivery.protection -> delivery.master ""

        // 2 — update the container image, authenticated secret-lessly.
        delivery.master -> delivery.image "2"
        delivery.entra -> delivery.image ""

        // 3 — GitOps updates the pods; everything pulls.
        delivery.argo -> delivery.master ""
        delivery.argo -> delivery.aks "3"
        delivery.image -> delivery.aks ""
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container delivery "DevOps" "How does a commit become a running pod?" {
            include *
            // PHASE ONE. Comment out before hand-arranging, and leave it commented.
            autoLayout lr 100 90
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
                strokeWidth 8
                color #5F5E5A
                fontSize 38
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
            element "CICD" {
                background #639922
                color #ffffff
                shape RoundedBox
            }
            element "Microsoft Azure - Azure Active Directory" {
                background #BA7517
                color #ffffff
            }
            element "Microsoft Azure - Container Registries" {
                background #185FA5
                color #ffffff
            }
            element "Microsoft Azure - Kubernetes Services" {
                background #185FA5
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
