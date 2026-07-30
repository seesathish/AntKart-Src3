# 19 · Delivery architecture — commit to running pod

> **Question:** How does a commit become a running pod?

The end-to-end overview — composing diagrams 14 (CI), 15 (CD), and 16 (GitOps) into one ten-second picture. Detail lives in those three.

```mermaid
flowchart TD
    DEV["Developer commit"]:::external
    PR["Pull request"]:::cicd
    CI["CI quality gate<br/>build · test · SonarCloud · Trivy<br/>4 required checks + branch protection"]:::cicd
    MERGE["Merge to master"]:::cicd
    OIDC{{"CD authenticates to Azure<br/>OIDC federated credential · no stored secret"}}:::identity
    BUILD["Build image · tag = commit SHA"]:::cicd
    ACR["Push to Azure Container Registry"]:::cicd
    BUMP["Bump image tag in Helm values in Git<br/>CD_PUSH_TOKEN · [skip ci]"]:::cicd
    ARGO["Argo CD detects drift"]:::cicd
    SYNC["Auto-sync + self-heal"]:::cicd
    POD["New pod on AKS"]:::service

    DEV --> PR --> CI --> MERGE --> OIDC --> BUILD --> ACR --> BUMP --> ARGO --> SYNC --> POD

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

- **One straight line, three concerns:** the pull-request **gate** (diagram 14), the merge-triggered **delivery** (diagram 15), and the in-cluster **reconciliation** (diagram 16) chain into a single path — nothing branches.
- **The gate is real:** merge is blocked until four required checks pass under branch protection (build-test, sonar, trivy, SonarCloud Code Analysis) — see diagram 14.
- **Secret-less and immutable:** CD authenticates by OIDC federated credential (no stored secret) and tags the image with the commit SHA — see diagram 15.
- **Delivery is a Git commit, not a push to the cluster:** CD bumps the image tag in the Helm values with `CD_PUSH_TOKEN` and `[skip ci]`; Argo CD then detects drift and auto-syncs with self-heal — see diagram 16.
- **Proven for all six services, hands-free** — a commit reaches a running pod with no manual step.
