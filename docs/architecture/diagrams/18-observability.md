# 18 · Observability pipeline

> **Question:** How do logs, metrics, and traces flow to their sinks and dashboards?

Delivered today: Serilog → Console → Application Insights / Log Analytics. Metrics and tracing are **planned**.

```mermaid
flowchart LR
    subgraph DELIVERED["Delivered"]
        SVC["Services + Functions<br/>Serilog structured logs"]:::service
        CON["Console sink"]:::cicd
        AI["Application Insights"]:::paas
        LA["Log Analytics · KQL"]:::paas
    end

    subgraph PLANNED["Planned"]
        OTEL["OpenTelemetry tracing"]:::issue
        PROM["Prometheus metrics"]:::issue
        GRAF["Grafana dashboards"]:::issue
    end

    SVC --> CON --> AI --> LA
    SVC -. "planned" .-> OTEL -.-> AI
    SVC -. "planned" .-> PROM -.-> GRAF

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:#FFFFFF,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

## What to notice

- **Logging is delivered:** every service and Function emits **Serilog** structured logs to the **Console**, collected in the cloud by **Application Insights / Log Analytics** and queried with **KQL** — no code-side sink credentials.
- **No Elasticsearch/Kibana:** the console stream is the transport; the collector is Azure Monitor, not an ELK stack.
- **Tracing and metrics are planned (red):** OpenTelemetry tracing, Prometheus metrics, and Grafana dashboards are **not** wired yet — drawn as red-dashed planned nodes.
- **Two prospective sinks:** planned traces would flow to Application Insights alongside logs; planned metrics would flow Prometheus → Grafana.
