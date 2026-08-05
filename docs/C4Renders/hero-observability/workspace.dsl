/*
 * AntKart — Observability (hero diagram)
 *
 * Question: how do you know it is working?
 *
 * Each service emits two kinds of telemetry, which travel by different routes and land
 * in the same Log Analytics workspace:
 *
 *   LOGS    Serilog writes one JSON line per event to stdout. The Azure Monitor agent
 *           runs on every node, reads container stdout, and ships it to the workspace,
 *           where it lands in the ContainerLog table.
 *
 *   TRACES  The OpenTelemetry SDK records a span for every request, database call and
 *           message. Spans are exported to Application Insights, which is workspace-based,
 *           so they land in the AppRequests and AppDependencies tables of the same workspace.
 *
 * Because both arrive in one workspace, a single KQL query can join a log line to the
 * span it belongs to: Serilog writes TraceId onto every line, and Application Insights
 * records the same value as OperationId.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-observability:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON to get the two-lane left-to-right flow.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange — logs lane
 *              along the top, traces lane below it, both meeting at the workspace.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — observability" "Two telemetry routes from the same services, landing in one queryable workspace" {

    !identifiers hierarchical

    model {

        services = softwareSystem "AntKart services" "Six .NET 9 services on AKS." {
            tags "Service"
        }

        group "Logs" {
            serilog = softwareSystem "Serilog" "Writes each log event as a single JSON line to stdout, carrying TraceId, SpanId, CorrelationId, ServiceName and Environment." {
                tags "Infra"
            }
            amaAgent = softwareSystem "Azure Monitor agent" "Runs on every node. Reads container stdout and ships it to the workspace." {
                tags "Infra"
            }
        }

        group "Traces" {
            otel = softwareSystem "OpenTelemetry SDK" "Records a span for every incoming request, outgoing HTTP and gRPC call, message publish and consume, and database query." {
                tags "Managed"
            }
            appInsights = softwareSystem "Application Insights" "Receives the spans and writes them to its workspace." {
                tags "Managed"
            }
        }

        logAnalytics = softwareSystem "Log Analytics workspace" "Stores both. ContainerLog holds the log lines; AppRequests and AppDependencies hold the spans. Queried with KQL. A log line's TraceId equals a span's OperationId, so the two can be joined." {
            tags "Datastore"
        }

        // Logs route.
        services -> serilog "Log events"
        serilog -> amaAgent "JSON lines on stdout"
        amaAgent -> logAnalytics "Writes ContainerLog"

        // Traces route.
        services -> otel "Requests, calls, queries"
        otel -> appInsights "Exports spans"
        appInsights -> logAnalytics "Writes AppRequests and AppDependencies"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        systemLandscape "Observability" "Logs and traces leave the same services by different routes and meet in one workspace, where a shared trace identifier joins them." {
            include *
            // PHASE ONE: autoLayout is ON. Before hand-arranging, COMMENT OUT the next
            // line, refresh, then drag. Leave it commented once you start dragging.
            autoLayout lr 120 110
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
