# 17 · Environment promotion — dev vs QA

> **Question:** How does a change move from dev to QA, and what differs between them?

**QA does not exist yet — `environments/qa` is planned.** This shows the state-isolation concern it must solve.

```mermaid
flowchart LR
    MODULES["infrastructure/modules<br/>(shared, environment-agnostic)"]:::cicd

    subgraph DEV["environments/dev — delivered"]
        DUNITS["18 Terragrunt units (dev inputs)"]:::cicd
        DSTATE[("state key = path_relative_to_include<br/>e.g. aks/terraform.tfstate")]:::datastore
    end

    subgraph QA["environments/qa — PLANNED"]
        QUNITS["same modules, qa inputs"]:::cicd
        QSTATE[("state key = path_relative_to_include<br/>e.g. aks/terraform.tfstate")]:::datastore
    end

    RISK["State-key collision risk:<br/>the key derives from the unit path only, NOT the environment.<br/>QA must use a distinct backend container or key prefix,<br/>or its state overwrites dev."]:::issue
    PLAN["QA planned — not yet created"]:::issue

    MODULES --> DUNITS
    MODULES --> QUNITS
    DUNITS --> DSTATE
    QUNITS --> QSTATE
    RISK -.-> QSTATE
    PLAN -.-> QA

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

- **Modules are shared, environments differ only by inputs:** promotion means a second `environments/qa` tree that reuses `infrastructure/modules` with QA-specific inputs — not copied module code.
- **The state key is the risk (red):** `root.hcl` derives the backend key from `path_relative_to_include()` — the **unit path only**, with no environment segment. A QA tree using the same backend container would produce identical keys (`aks/terraform.tfstate`, …) and **overwrite dev state**.
- **The fix QA must adopt:** a distinct backend `container_name` or a per-environment key prefix, so dev and QA state can never collide.
- **QA is planned, not built** — both the QA subgraph and the risk are drawn as red-dashed to keep that explicit.
