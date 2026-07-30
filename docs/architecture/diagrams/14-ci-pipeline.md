# 14 · CI pipeline

> **Question:** What runs on a pull request, and what gates the merge?

Drawn from the real `*-ci.yml` workflows (e.g. `.github/workflows/products-ci.yml`).

```mermaid
flowchart TD
    PR["Pull request → master<br/>path-filtered (service + BuildingBlocks + tests)"]:::cicd

    subgraph CI["service-ci.yml"]
        BT["job: build-test<br/>restore · build Release · unit + integration tests · OpenCover"]:::cicd
        SN["job: sonar (needs build-test)<br/>dotnet-sonarscanner begin/build/test/end"]:::cicd
        TR["job: trivy<br/>fs scan + Dockerfile scan · HIGH,CRITICAL · exit 1"]:::cicd
    end

    BP{{"Branch protection: master-protection<br/>required: build-test · sonar · trivy · SonarCloud Code Analysis"}}:::identity
    MERGE["Merge allowed only when all four checks are green"]:::cicd

    PR --> BT
    BT --> SN
    PR --> TR
    BT --> BP
    SN --> BP
    TR --> BP
    BP --> MERGE

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

- **Three jobs, real names:** `build-test`, `sonar`, `trivy` — exactly the job names in the workflow.
- **`sonar` depends on `build-test`** (`needs:`), so analysis is not spent on a commit that doesn't compile; `trivy` runs in parallel.
- **Four required checks, not three:** the `master-protection` ruleset requires `build-test`, `sonar`, `trivy`, **and** `SonarCloud Code Analysis` (SonarCloud's own PR quality-gate status).
- **CI is a pure gate:** it builds and tests but pushes no image and touches no cluster — delivery is the CD workflow (diagram 15).
- **Path filters keep it per-service:** a PR touching only one service runs only that service's CI.
