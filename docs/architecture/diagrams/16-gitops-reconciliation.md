# 16 · GitOps reconciliation loop

> **Question:** How does a Git change become a running pod via Argo CD?

```mermaid
flowchart LR
    GIT[("Git · master<br/>deploy/helm/values/service.yaml")]:::cicd
    ARGO["Argo CD Application controller<br/>(in-cluster)"]:::cicd
    LIVE["Live cluster state<br/>Deployments / Services"]:::paas
    IMG["ACR image :sha"]:::cicd
    POLICY{{"syncPolicy on all six Apps<br/>automated · selfHeal: true · prune: false"}}:::identity

    GIT -->|watch| ARGO
    ARGO -->|compare desired vs live| LIVE
    ARGO -->|automated sync| LIVE
    LIVE -.->|drift detected| ARGO
    ARGO -->|selfHeal: revert to Git| LIVE
    LIVE -->|pull| IMG
    POLICY -.-> ARGO

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

- **Pull, not push:** Argo CD runs *inside* the cluster and watches Git — nothing outside the cluster deploys, and CD holds no cluster credentials.
- **Auto-sync closes the loop:** with `automated` on, the CD tag-bump commit (diagram 15) deploys hands-free — no manual sync.
- **Self-heal makes Git authoritative:** live drift (a manual `kubectl edit`) is reverted back to Git; the sync policy is declared in Git on all six Applications.
- **Prune is off by design (`prune: false`):** Argo won't delete resources that disappear from Git — a conservative default recorded in the manifests.
- **The image is pulled by tag:** the Deployment references `:sha`, so the new pod runs exactly the image the commit named.
