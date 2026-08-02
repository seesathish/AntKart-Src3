# Observability — how it is seen

Observability is **mostly delivered**: structured logging and distributed tracing are in place across every service; metrics are *exposed* but not yet scraped. Two secret-less paths ship today — Serilog writes JSON logs to the console, collected by the AKS OMS agent into **Log Analytics** (`ContainerLog`), and OpenTelemetry exports **traces to Application Insights** (`AppRequests` / `AppDependencies`). A Prometheus `/metrics` endpoint is exposed on every service, but nothing scrapes it yet.

## Observability pipeline

```mermaid
flowchart TD
    subgraph DELIVERED["Delivered"]
        SVC["Services + Functions<br/>Serilog JSON + OpenTelemetry"]:::service
        CON["Console (stdout)"]:::cicd
        OMS["AKS OMS agent"]:::paas
        LA["Log Analytics<br/>ContainerLog · KQL"]:::paas
        AI["Application Insights<br/>AppRequests / AppDependencies"]:::paas
        METRICS["/metrics<br/>Prometheus format (exposed)"]:::service
    end

    subgraph PLANNED["Planned — not deployed"]
        PROM["Prometheus scrape"]:::issue
        GRAF["Grafana dashboards"]:::issue
    end

    SVC -->|"Serilog logs"| CON --> OMS --> LA
    SVC -->|"OTel traces"| AI
    SVC -->|"exposes"| METRICS
    METRICS -. "nothing scrapes it yet" .-> PROM -.-> GRAF

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

**What to notice**

- **Logging is delivered — via the OMS agent into Log Analytics, NOT Application Insights.** Every service and Function emits **Serilog** `RenderedCompactJsonFormatter` JSON to the **console**; the AKS **OMS agent** ships container stdout into **Log Analytics**, landing in the legacy **`ContainerLog`** table (the cluster is on the pre-DCR collection path — `useAADAuth=false`, no data collection rules, so not `ContainerLogV2`). Because each line is JSON, `ServiceName`, `Environment`, `CorrelationId` and `TraceId`/`SpanId` are queryable via `parse_json(LogEntry)`. No code-side sink credentials.
- **Tracing is delivered — to Application Insights.** All six services export **OpenTelemetry** traces via the Azure Monitor exporter; spans land in **`AppRequests`** (server) and **`AppDependencies`** (client). Instrumented: AspNetCore, HttpClient, gRPC client, MassTransit (Service Bus), Npgsql (PostgreSQL), MongoDB (Cosmos Mongo API) and StackExchange.Redis.
- **Logs stay on Serilog, not OTel.** `AppTraces` contains only `func-antkart-notifications-dev` (the Functions app's classic App Insights SDK); the six AKS services deliberately do **not** enable OTel log export — Serilog already covers logs, and logs are joined to traces by the `TraceId`/`SpanId` enrichment instead. See [ADR-025](../adr/ADR-025-observability-architecture.md).
- **Metrics are exposed but not scraped.** Each service serves `/metrics` in Prometheus exposition format, but **nothing scrapes it yet** — Prometheus and Grafana are not deployed. (Note: `AK.Discount.Grpc`'s `/metrics` is not reachable by a standard scrape at all — its Kestrel endpoint is HTTP/2-only for gRPC; see [KI-008](../KNOWN_ISSUES.md).)
- **Correlation and trace-linking are in place.** The `X-Correlation-Id` middleware follows a request across services in the logs; OTel additionally propagates W3C `traceparent` across HttpClient/gRPC/MassTransit, and each log line carries `TraceId`/`SpanId` so a slow span in Application Insights pivots to its log lines.

## Working KQL

Run against the `log-antkart-dev` workspace. On **Windows**, Azure CLI strips inner double quotes, so KQL **string literals must use single quotes** when passed via `--analytics-query`.

**Structured logs with correlation** (from the JSON console stream in `ContainerLog`):

```kql
ContainerLog | where TimeGenerated > ago(1h)
| extend L=parse_json(LogEntry)
| where isnotempty(L.CorrelationId)
| project TimeGenerated, tostring(L.ServiceName),
          tostring(L.CorrelationId), tostring(L['@m'])
```

**Which services are reporting traces:**

```kql
AppRequests | summarize Requests=count(),
              Failed=countif(Success == false) by AppRoleName
```

**Dependencies by type** (postgresql, mongodb, redis, servicebus, gRPC):

```kql
AppDependencies | summarize Calls=count(), AvgMs=round(avg(DurationMs),1)
                  by AppRoleName, DependencyType, Target
```

**A distributed trace spanning services:**

```kql
union AppRequests, AppDependencies
| summarize Services=make_set(AppRoleName), Spans=count() by OperationId
| where array_length(Services) > 1
```

## How it was built

- The architecture and the decisions behind it — Serilog for logs, OpenTelemetry for traces/metrics, why OTel log export is off, and the OTel version pin — are in [ADR-025 — Observability Architecture](../adr/ADR-025-observability-architecture.md).
- Health-probe surfaces (`/health/live`, `/health/ready`, `/health/deps`) are a complementary signal — see the health-check wiring in [AK.BuildingBlocks](../../AK.BuildingBlocks/BUILDING_BLOCKS.md). _(For historical background only, the superseded [observability concepts](../guides/observability-concepts.md) note describes the earlier Phase-1 intent.)_

## Decisions

- [ADR-013 — Key Vault RBAC and Observability Foundation](../adr/ADR-013-key-vault-rbac-and-observability-foundation.md)
- [ADR-025 — Observability Architecture](../adr/ADR-025-observability-architecture.md)

## Open items

- **Metrics are exposed but not scraped** — Prometheus and Grafana are not deployed; the scrape/dashboards remain on the [Roadmap](../ROADMAP.md).
- **`AK.Discount.Grpc` `/metrics` is unreachable over h2c** — see [KI-008](../KNOWN_ISSUES.md).
