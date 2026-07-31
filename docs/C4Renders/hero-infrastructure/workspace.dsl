/*
 * AntKart — Infrastructure as code (hero diagram)
 *
 * Question: how does the cloud get built, and how is it promoted to a new environment?
 *
 * THE SHAPE — two reusable things on the left (the shared modules and the one root
 * configuration) feed the ENVIRONMENTS in the middle. dev is delivered (solid green);
 * qa is next (dashed). Both compose the SAME modules and inherit the SAME root config —
 * only their inputs differ. On the right, an apply writes isolated per-unit state and
 * provisions the real Azure resources. The verbs live on the arrows and the README
 * legend; Terraform commands (init/plan/apply) are deliberately absent — they are
 * actions, and drawing them turns an architecture diagram into a runbook.
 *
 * THE THREE IDEAS
 *   1. One root configuration generates the backend/provider/versions into every unit.
 *   2. Each environment's units compose the shared modules with that environment's
 *      inputs — so a new environment is new inputs, not new code.
 *   3. State is isolated per unit, leased during an apply, and kept in a resource group
 *      of its own, so destroying the platform never destroys the record of it.
 *
 * QA IS NEXT — it is drawn now (dashed) so the promotion story is visible: qa reuses
 * the modules and root, with qa inputs. NOTE for when it is built: the state key is the
 * UNIT PATH only, so qa MUST use a distinct backend container or key prefix or it will
 * overwrite dev state. That caveat belongs in the README legend, not a box.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-infrastructure:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *
 * TWO-PHASE: autoLayout ON for rough ranks, then COMMENT IT OUT before dragging.
 * Target layout: shared modules and root configuration stacked on the left; the two
 * environments stacked in the centre (dev above, qa below and dashed); state storage
 * and Azure resources on the right. qa mirrors dev exactly — every dev arrow has a
 * parallel dashed qa arrow below it, so the two rows read as "the same shape, one
 * delivered and one planned".
 */

workspace "AntKart — infrastructure as code" "How the cloud gets built and promoted: one root config, shared modules, per-environment units, isolated state" {

    !identifiers hierarchical

    model {

        iac = softwareSystem "AntKart infrastructure" "" {

            modules = container "Shared modules" "" "reusable · versioned" {
                tags "Infra"
            }
            root = container "Root configuration" "" "root.hcl · backend · provider · versions" {
                tags "Edge"
            }

            group "environments" {
                dev = container "environments/dev" "" "18 units · dev inputs · delivered" {
                    tags "Service"
                }
                qa = container "environments/qa" "" "same modules · qa inputs · planned" {
                    tags "Planned"
                }
            }

            state = container "State storage" "" "one blob per unit · leased · own resource group" {
                tags "Datastore"
            }
            resources = container "Azure resources" "" "one estate per environment" {
                tags "Managed"
            }
        }

        // 1 — one root config generates the boilerplate into every unit (both envs).
        iac.root -> iac.dev "1"
        iac.root -> iac.qa "" {
            tags "Planned"
        }

        // 2 — each environment composes the shared modules with its own inputs.
        iac.modules -> iac.dev "2"
        iac.modules -> iac.qa "" {
            tags "Planned"
        }

        // 3 — an apply isolates per-unit state and provisions the resources. Both
        // environments do this: dev delivered (solid), qa planned (dashed).
        iac.dev -> iac.state "3"
        iac.dev -> iac.resources "creates"
        iac.qa -> iac.state "" {
            tags "Planned"
        }
        iac.qa -> iac.resources "" {
            tags "Planned"
        }
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container iac "InfrastructureAsCode" "How does the cloud get built, and how is it promoted?" {
            include *
            // PHASE ONE. Comment out before hand-arranging, and leave it commented.
            autoLayout lr 220 140
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
            element "Infra" {
                background #888780
                color #ffffff
            }
            element "Managed" {
                background #378ADD
                color #ffffff
            }
            element "Datastore" {
                background #185FA5
                color #ffffff
                shape Cylinder
            }
            element "Planned" {
                background #5F5E5A
                color #ffffff
                border dashed
                strokeWidth 6
            }
            relationship "Relationship" {
                dashed false
                thickness 4
                fontSize 40
                routing Orthogonal
            }
            relationship "Planned" {
                dashed true
                thickness 4
                fontSize 40
                routing Orthogonal
            }
        }
    }
}
