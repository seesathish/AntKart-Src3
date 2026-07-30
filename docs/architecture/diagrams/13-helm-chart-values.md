# 13 · Helm chart & values precedence

> **Question:** How does one generic chart become six services, and which values win?

```mermaid
flowchart TD
    CHART["deploy/helm/antkart-service<br/>one generic chart · values.yaml defaults"]:::cicd

    subgraph VALUES["six per-service values files (deploy/helm/values)"]
        V1["products.yaml"]:::cicd
        V2["cart.yaml · image.name=shoppingcart"]:::cicd
        V3["order.yaml"]:::cicd
        V4["payments.yaml"]:::cicd
        V5["discount.yaml · gRPC / TCP probes"]:::cicd
        V6["gateway.yaml · ingress + ocelot config"]:::cicd
    end

    PARAM{{"Argo CD Application helm.parameters<br/>e.g. gateway ingress.host / clusterIssuer, image.tag"}}:::identity
    RENDER["Rendered manifests → applied by Argo CD"]:::paas
    PREC["Precedence low→high:<br/>chart values.yaml  <  per-service values  <  Application parameters"]:::cicd

    CHART -->|deep-merged under| VALUES
    VALUES -->|overridden by| PARAM
    PARAM --> RENDER
    PREC -.-> PARAM

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

- **One chart, six releases:** every service instantiates the *same* `antkart-service` chart — there is no per-service chart to keep in step.
- **Precedence is three-layer, low→high:** chart `values.yaml` defaults, then the per-service values file, then the Argo CD Application's `helm.parameters` win last.
- **The special cases live in the values files:** `cart.yaml` overrides `image.name` to `shoppingcart`; `discount.yaml` selects TCP probes for its h2c gRPC port; `gateway.yaml` carries the ingress + Ocelot config.
- **The gateway's environment-specific bits are parameters, not values:** `ingress.host` (`api.antkart.in`) and `clusterIssuer` come in as Application parameters, so Git — via Argo CD — is authoritative and self-heal can't revert them.
