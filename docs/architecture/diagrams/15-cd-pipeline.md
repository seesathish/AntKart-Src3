# 15 · CD pipeline

> **Question:** What happens on merge — build, push, tag-bump — and with what identity?

Drawn from the real `*-cd.yml` workflows (e.g. `.github/workflows/products-cd.yml`).

```mermaid
flowchart TD
    MERGE["Push to master · path-filtered<br/>service-cd.yml"]:::cicd

    subgraph J1["job: build-and-push"]
        TAG["compute tag = GITHUB_SHA first 7"]:::cicd
        OIDC{{"azure/login · OIDC · no secret<br/>id-ak-cicd-dev"}}:::identity
        BUILD["az acr login → docker build → push<br/>acrantkartdev/antkart/service:sha"]:::cicd
    end

    subgraph J2["job: update-gitops (needs build-and-push)"]
        CO["checkout master · token = CD_PUSH_TOKEN"]:::cicd
        YQ["yq: set .image.tag in deploy/helm/values/service.yaml"]:::cicd
        PUSH["commit + push · chore(cd) image → sha · [skip ci]"]:::cicd
    end

    ARGO["Argo CD reconciles (diagram 16)"]:::cicd

    MERGE --> TAG --> OIDC --> BUILD --> CO --> YQ --> PUSH --> ARGO

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

- **Secret-less Azure auth:** `azure/login` uses OIDC federated with `id-ak-cicd-dev` (repo *variables*, not secrets) — the only Azure privilege is AcrPush.
- **Immutable tag:** the image is tagged with the **short commit SHA**, so a tag always means exactly one build.
- **Delivery is a Git commit, not a deploy:** job 2 bumps `.image.tag` in the service's values file and pushes — no `helm`/`kubectl`, no cluster credentials.
- **The push needs a PAT:** `update-gitops` checks out with `CD_PUSH_TOKEN` (the github-actions bot isn't a bypass actor), and the commit carries `[skip ci]`.
- **Loop-safe:** the tag-bump touches only `deploy/helm/values/**`, which is outside CD's path filter, so it can't retrigger CD.
