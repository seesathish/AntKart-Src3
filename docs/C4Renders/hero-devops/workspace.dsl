/*
 * AntKart — DevOps (hero diagram)
 *
 * Question: how does a commit become a running pod?
 * Models the delivery pipeline as containers grouped Source → Quality gate →
 * Build and publish → GitOps, sourced from the twelve GitHub Actions workflows
 * (.github/workflows, a CI + CD pair per service) and deploy/argocd. CD never touches
 * the cluster; it commits a tag bump to Git and Argo CD reconciles.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-devops:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get the left-to-right pipeline flow.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange the four
 *              stages left to right with the developer at the far left and the pod at
 *              the far right.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — DevOps" "How a commit becomes a running pod: CI quality gate, secret-less CD, GitOps reconciliation" {

    !identifiers hierarchical

    model {

        developer = person "Developer" "Commits code and opens a pull request." {
            tags "Person"
        }

        gitRepo = softwareSystem "Git repository (master)" "Application source plus deploy/helm/values — Argo CD's desired state." {
            tags "External"
        }

        acr = softwareSystem "Azure Container Registry" "acrantkartdev — immutable, commit-SHA-tagged images." {
            tags "Managed"
        }

        pipeline = softwareSystem "AntKart delivery pipeline" "Twelve GitHub Actions workflows — a CI + CD pair per service — that carry a commit to a running pod." {

            group "Source" {
                pr = container "Pull request → master" "Path-filtered to the service, AK.BuildingBlocks and tests." "GitHub Actions" {
                    tags "CICD"
                }
            }

            group "Quality gate (service-ci.yml)" {
                buildtest = container "build-test" "restore · build Release · unit + integration tests · OpenCover coverage." "job" {
                    tags "CICD"
                }
                sonar = container "sonar" "SonarCloud analysis. needs build-test." "job" {
                    tags "CICD"
                }
                trivy = container "trivy" "Filesystem + Dockerfile scan; HIGH/CRITICAL fails the build." "job" {
                    tags "CICD"
                }
                branchProtection = container "Branch protection — master-protection" "Merge blocked until four required checks are green: build-test, sonar, trivy, SonarCloud Code Analysis." "ruleset" {
                    tags "Identity"
                }
            }

            group "Build & publish (service-cd.yml)" {
                cdOidc = container "azure/login (OIDC)" "id-ak-cicd-dev federated credential — no stored secret, AcrPush only." "job step" {
                    tags "Identity"
                }
                buildImage = container "Build image · tag = commit SHA" "docker build with an immutable short-SHA tag." "job step" {
                    tags "CICD"
                }
                tagBump = container "Bump image tag in Git" "yq sets .image.tag in deploy/helm/values; commit [skip ci] via CD_PUSH_TOKEN." "job step" {
                    tags "CICD"
                }
            }

            group "GitOps" {
                argo = container "Argo CD" "Detects drift and auto-syncs (self-heal, prune false) across six Applications." "in-cluster" {
                    tags "CICD"
                }
                pod = container "New pod on AKS" "Runs exactly the image the commit named (:sha)." "workload" {
                    tags "Service"
                }
            }
        }

        // ── The path: commit → gate → merge → build → tag bump → reconcile → pod ──
        developer -> gitRepo "Commits & opens PR"
        developer -> pipeline.pr "Opens"
        gitRepo -> pipeline.pr "Triggers CI"
        pipeline.pr -> pipeline.buildtest "Runs"
        pipeline.buildtest -> pipeline.sonar "then"
        pipeline.pr -> pipeline.trivy "Runs"
        pipeline.buildtest -> pipeline.branchProtection "Reports check"
        pipeline.sonar -> pipeline.branchProtection "Reports check"
        pipeline.trivy -> pipeline.branchProtection "Reports check"
        pipeline.branchProtection -> gitRepo "Merge to master (only when green)"
        gitRepo -> pipeline.cdOidc "Push to master triggers CD"
        pipeline.cdOidc -> pipeline.buildImage "Authenticated build"
        pipeline.buildImage -> acr "Pushes :sha"
        pipeline.buildImage -> pipeline.tagBump "then"
        pipeline.tagBump -> gitRepo "Commits tag bump ([skip ci])"
        gitRepo -> pipeline.argo "Watched by"
        pipeline.argo -> pipeline.pod "Auto-sync + self-heal deploys"
        acr -> pipeline.pod "Image pulled by"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container pipeline "DevOps" "How does a commit become a running pod? Source, quality gate, build & publish, GitOps." {
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
