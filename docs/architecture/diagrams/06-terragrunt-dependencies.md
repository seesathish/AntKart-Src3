# 06 · Terragrunt unit dependency graph

> **Question:** In what order do the infrastructure units apply, and what does each depend on?

Arrows read **"depends on"** — the target applies first. Drawn from the real `dependency` blocks in `infrastructure/environments/dev/*/terragrunt.hcl`.

```mermaid
flowchart TD
    RG["resource-group"]:::paas
    APPREG["app-registration<br/>(independent · directory-plane)"]:::identity
    AKS["aks"]:::paas
    FN["function-app"]:::paas
    OIDC["github-oidc"]:::identity
    WI["workload-identity"]:::identity
    RA["role-assignments"]:::identity

    subgraph RGONLY["11 units — depend only on resource-group"]
        NET["networking"]:::paas
        ACR["container-registry"]:::paas
        KV["key-vault"]:::identity
        OBS["observability"]:::paas
        COS["cosmosdb"]:::datastore
        PG["postgresql"]:::datastore
        RED["redis"]:::datastore
        SB["servicebus"]:::paas
        EVG["eventgrid"]:::paas
        ACS["communication-services"]:::paas
        GOV["governance"]:::paas
    end

    RGONLY --> RG
    AKS --> RG
    AKS --> NET
    AKS --> ACR
    AKS --> OBS
    FN --> RG
    FN --> OBS
    OIDC --> RG
    OIDC --> ACR
    WI --> RG
    WI --> AKS
    WI --> KV
    WI --> SB
    WI --> EVG
    RA --> FN
    RA --> KV
    RA --> SB
    RA --> EVG

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

- **18 live units, not 19.** `environments/dev` currently holds **18** unit folders (the [Diagram Plan](../DIAGRAM-PLAN.md) says 19 — off by one; the extra is the state-backend bootstrap, which is an `az` step, not a Terragrunt unit).
- **`resource-group` is the root** — everything except `app-registration` traces back to it; **11 units depend on it alone** and nothing else.
- **`app-registration` is independent** — it manages a directory object via the `azuread` provider, not a resource-group resource.
- **Two composite tails carry the interesting order:** `role-assignments` waits on `function-app` + `key-vault` + `servicebus` + `eventgrid`; `workload-identity` waits on `aks` (for its OIDC issuer) + `key-vault` + `servicebus` + `eventgrid`.
- This exceeds the ~15-node guideline because a *complete* dependency graph must show every unit; the 11 resource-group-only units are grouped to keep it legible.
