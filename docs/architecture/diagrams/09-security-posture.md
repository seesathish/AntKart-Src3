# 09 · Security posture & trust boundaries

> **Question:** What is exposed vs internal, and where do the known gaps sit?

```mermaid
flowchart LR
    Client["Client"]:::external

    subgraph PUBLIC["Public — internet-facing"]
        GW["AK.Gateway<br/>Entra JWT validated"]:::edge
    end

    subgraph CLUSTER["ClusterIP-only — internal"]
        P["AK.Products<br/>Entra JWT validated"]:::service
        O["AK.Order<br/>Entra JWT validated"]:::service
        PAY["AK.Payments<br/>Entra JWT validated"]:::service
        C["AK.ShoppingCart<br/>Entra JWT validated"]:::service
        D["AK.Discount (gRPC)<br/>decodes JWT — no signature check"]:::service
    end

    KI["KI-002 · Discount validates no signature/issuer/audience<br/>mitigation: ClusterIP-only, not exposed"]:::issue

    Client -->|HTTPS + Bearer| GW
    GW --> P
    GW --> O
    GW --> PAY
    GW --> C
    P -->|gRPC| D
    KI -.-> D

    classDef external fill:#B4B2A9,stroke:#7A7870,color:#111,stroke-dasharray:4 3;
    classDef service fill:#1D9E75,stroke:#14795A,color:#FFF;
    classDef paas fill:#0078D4,stroke:#005A9E,color:#FFF;
    classDef datastore fill:#185FA5,stroke:#0F3F6E,color:#FFF;
    classDef identity fill:#BA7517,stroke:#8A560F,color:#FFF;
    classDef edge fill:#7F77DD,stroke:#5B52B8,color:#FFF;
    classDef cicd fill:#639922,stroke:#496F18,color:#FFF;
    classDef issue fill:none,stroke:#E24B4A,color:#E24B4A,stroke-dasharray:5 4;
```

## What to notice

- **Exactly one thing is public:** only `AK.Gateway` is internet-facing (behind the ingress); the five backing services are **ClusterIP-only** and unreachable from outside the cluster.
- **Defence in depth on tokens:** the Entra bearer token is validated at the gateway **and again** inside each REST service — the gateway is not a trust boundary the services rely on.
- **KI-002 (red):** `AK.Discount` (gRPC) **decodes** the JWT but does **not** verify signature, issuer, audience, or expiry — a real gap, drawn as a red-dashed annotation.
- **Why KI-002 is only Medium today:** Discount is ClusterIP-only and reached solely by `AK.Products` over cluster DNS, so the weak check is not externally reachable — the mitigation, not a fix.
