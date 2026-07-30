# 08 · Identity & trust chain

> **Question:** How does trust flow from provisioning to runtime, secret-lessly?

From the Terraform service principal, through the real Terraform modules, to the identities and grants they create.

```mermaid
flowchart TD
    SP["Terraform service principal<br/>sp-antkart-terraform-dev<br/>(ARM_* env vars)"]:::identity

    subgraph OIDC["module: github-oidc"]
        FED["GitHub federated credential<br/>token.actions.githubusercontent.com"]:::identity
        CICD["id-ak-cicd-dev (UAMI)"]:::identity
        ACRPUSH{{"AcrPush on ACR"}}:::identity
    end

    subgraph WI["module: workload-identity"]
        AKSFED["federated to AKS OIDC issuer<br/>subject system:serviceaccount:antkart:ak-service"]:::identity
        UAMI["id-ak-service-dev (UAMI x6)"]:::identity
        ROLES{{"Key Vault Secrets User<br/>Service Bus Data Sender/Receiver<br/>EventGrid Data Sender"}}:::identity
    end

    subgraph RA["module: role-assignments"]
        FNROLES{{"Function App identity grants<br/>Key Vault / Service Bus / Event Grid"}}:::identity
    end

    SP -->|provisions| OIDC
    SP -->|provisions| WI
    SP -->|provisions| RA
    FED --> CICD --> ACRPUSH
    AKSFED --> UAMI --> ROLES

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

- **One provisioning identity:** the `sp-antkart-terraform-dev` service principal (authenticated via `ARM_*` env vars) creates all three identity modules; it is the only credential-bearing principal, and it lives outside the running platform.
- **CI/CD gets exactly one privilege:** `github-oidc` mints `id-ak-cicd-dev` with **AcrPush only** — it can push images, nothing else, and holds no secret (GitHub OIDC federation).
- **Runtime is per-service and least-privilege:** `workload-identity` creates six `id-ak-<service>-dev` UAMIs, each federated to the **AKS OIDC issuer** for its own ServiceAccount subject, granted only the data-plane roles it needs.
- **The Function App is separate:** `role-assignments` grants the Function App's identity its Key Vault / Service Bus / Event Grid roles — a distinct path from the six cluster services.
- **No stored secrets anywhere in the chain** — every arrow is a federation or an RBAC grant, not a key hand-off.
