/*
 * AntKart — Observability (hero diagram)
 *
 * Question: how do you know it is working?
 * Sourced from docs/development/5-observability.md, ADR-013 and docs/design/OBSERVABILITY.md.
 * MOST OF THIS IS NOT BUILT YET. Only the Serilog → Console → Application Insights →
 * Log Analytics path is delivered. OpenTelemetry tracing, Prometheus metrics and
 * Grafana dashboards are PLANNED — tagged "Planned" and styled distinctly. Do not
 * read the planned elements as if they exist.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-observability:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get the left-to-right flow.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange the
 *              delivered path along the top and the planned path below it.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — observability" "How you know it is working: structured logging delivered, tracing and metrics planned" {

    !identifiers hierarchical

    model {

        group "Delivered" {
            services = softwareSystem "AntKart services + Functions" "Every service and Function emits Serilog structured logs, each request carrying an X-Correlation-Id." {
                tags "Service"
            }
            console = softwareSystem "Console sink" "The transport — the console stream, with no code-side sink credentials." {
                tags "Infra"
            }
            appInsights = softwareSystem "Application Insights" "Collects the console stream in the cloud." {
                tags "Managed"
            }
            logAnalytics = softwareSystem "Log Analytics" "Central log store, queried with KQL. No Elasticsearch/Kibana." {
                tags "Managed"
            }
        }

        group "Planned" {
            otel = softwareSystem "OpenTelemetry tracing" "Planned — distributed traces. Not yet wired." {
                tags "Planned"
            }
            prometheus = softwareSystem "Prometheus metrics" "Planned — metric scraping. Not yet wired." {
                tags "Planned"
            }
            grafana = softwareSystem "Grafana dashboards" "Planned — metric dashboards. Not yet wired." {
                tags "Planned"
            }
        }

        // Delivered path (solid).
        services -> console "Serilog structured logs"
        console -> appInsights "Collected by"
        appInsights -> logAnalytics "Backed by (KQL)"

        // Planned paths (dashed).
        services -> otel "Traces (planned)" {
            tags "Planned"
        }
        otel -> appInsights "Exported to (planned)" {
            tags "Planned"
        }
        services -> prometheus "Metrics scraped (planned)" {
            tags "Planned"
        }
        prometheus -> grafana "Visualised in (planned)" {
            tags "Planned"
        }
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        systemLandscape "Observability" "How do you know it is working? Serilog to Azure Monitor is delivered; tracing and metrics are planned." {
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
